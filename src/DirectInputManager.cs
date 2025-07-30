using SharpDX.DirectInput;
using System.Runtime.InteropServices;

namespace RoWheelBridge;

public class DirectInputManager : IDisposable
{
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();
    
    private DirectInput _directInput;
    public DirectInput DirectInputInstance => _directInput;
    private Joystick? _wheel;
    private Effect? _forceEffect;
    private bool _disposed = false;
    private bool _forceFeedbackEnabled = false;
    private int _numForceFeedbackAxes = 0;
    
    public DirectInputManager()
    {
        _directInput = new DirectInput();
    }
    
    public List<DeviceInstance> GetWheelDevices(bool debugOutput = false)
    {
        var allDevices = _directInput.GetDevices(DeviceClass.All, DeviceEnumerationFlags.AllDevices)
            .Where(device => device.Type != DeviceType.Mouse && 
                           device.Type != DeviceType.Keyboard)
            .ToList();
        
        if (debugOutput)
            Console.WriteLine($"Found {allDevices.Count} non-mouse/keyboard devices");
        
        // Filter and prioritize actual steering wheel devices
        var filteredDevices = new List<DeviceInstance>();
        var seenProductNames = new HashSet<string>();
        var duplicateCount = 0;
        
        foreach (var device in allDevices)
        {
            // Count duplicates for debugging
            if (seenProductNames.Contains(device.ProductName))
            {
                duplicateCount++;
                if (debugOutput)
                    Console.WriteLine($"  Skipping duplicate: {device.ProductName}");
                continue;
            }
                
            if (IsMainWheelDevice(device, debugOutput))
            {
                if (debugOutput)
                    Console.WriteLine($"  Selected main device: {device.ProductName}");
                filteredDevices.Add(device);
                seenProductNames.Add(device.ProductName);
            }
            else
            {
                if (debugOutput)
                    Console.WriteLine($"  Filtered out: {device.ProductName} (not main wheel device)");
            }
        }
        
        // If filtering was too aggressive and we have no devices, fall back to original list
        // but still deduplicate by product name
        if (!filteredDevices.Any())
        {
            if (debugOutput)
                Console.WriteLine("No devices passed main wheel filter, using fallback with deduplication");
            seenProductNames.Clear();
            foreach (var device in allDevices)
            {
                if (!seenProductNames.Contains(device.ProductName))
                {
                    filteredDevices.Add(device);
                    seenProductNames.Add(device.ProductName);
                }
            }
        }
        
        if (debugOutput)
            Console.WriteLine($"Filtered {duplicateCount} duplicates, returning {filteredDevices.Count} unique devices");
        return filteredDevices;
    }
    
    private bool IsMainWheelDevice(DeviceInstance device, bool debugOutput = false)
    {
        try
        {
            // Create a temporary joystick to check capabilities
            using var tempJoystick = new Joystick(_directInput, device.InstanceGuid);
            var capabilities = tempJoystick.Capabilities;
            
            if (debugOutput)
                Console.WriteLine($"    Checking {device.ProductName}: {capabilities.AxeCount} axes, {capabilities.ButtonCount} buttons, FF: {capabilities.Flags.HasFlag(DeviceFlags.ForceFeedback)}");
            
            // Main wheel device typically has:
            // 1. Multiple axes (at least X for steering, often Y/Z for pedals)
            // 2. Multiple buttons (for paddle shifters, etc.)
            // 3. Possibly force feedback
            
            bool hasMultipleAxes = capabilities.AxeCount >= 3; // Steering + at least 2 pedal axes
            bool hasReasonableButtons = capabilities.ButtonCount >= 2; // At least a few buttons
            bool hasForceFeedback = capabilities.Flags.HasFlag(DeviceFlags.ForceFeedback);
            
            // Prioritize devices with force feedback (usually the main wheel)
            if (hasForceFeedback && capabilities.AxeCount >= 2 && hasReasonableButtons)
            {
                if (debugOutput)
                    Console.WriteLine($"    -> Selected (force feedback device)");
                return true;
            }
                
            // Otherwise, look for devices with good axis and button counts
            if (hasMultipleAxes && capabilities.ButtonCount >= 4)
            {
                if (debugOutput)
                    Console.WriteLine($"    -> Selected (multi-axis device with buttons)");
                return true;
            }
            
            // Also accept devices that are specifically racing wheel or joystick types
            if ((device.Type == DeviceType.Driving || device.Type == DeviceType.Joystick) && 
                capabilities.AxeCount >= 2 && hasReasonableButtons)
            {
                if (debugOutput)
                    Console.WriteLine($"    -> Selected (driving/joystick type)");
                return true;
            }
                
            if (debugOutput)
                Console.WriteLine($"    -> Rejected");
            return false;
        }
        catch (Exception ex)
        {
            if (debugOutput)
                Console.WriteLine($"    -> Error checking device: {ex.Message}");
            // If we can't check capabilities, include it as a fallback
            return false;
        }
    }
    
    public bool ConnectToWheel(Guid deviceGuid)
    {
        try
        {
            Console.WriteLine("Connecting to steering wheel...");
            
            CleanupResources();
            
            _wheel = new Joystick(_directInput, deviceGuid);
            Console.WriteLine($"Device: {_wheel.Information.ProductName}");
            
            // Check capabilities
            var capabilities = _wheel.Capabilities;
            bool hasForceFeedback = capabilities.Flags.HasFlag(DeviceFlags.ForceFeedback);
            Console.WriteLine($"Force Feedback Support: {(hasForceFeedback ? "Yes" : "No")}");
            
            var consoleWindow = GetConsoleWindow();
            if (consoleWindow == IntPtr.Zero)
            {
                Console.WriteLine("Warning: Could not get console window handle, using desktop window");
                consoleWindow = IntPtr.Zero; // Use desktop window
            }
            
            bool exclusiveAccessGranted = false;
            
            if (hasForceFeedback)
            {
                // Try to get exclusive access for force feedback
                Console.WriteLine("Attempting to get exclusive access for force feedback...");
                
                try
                {
                    _wheel.SetCooperativeLevel(consoleWindow, CooperativeLevel.Exclusive | CooperativeLevel.Foreground);
                    Console.WriteLine("Exclusive foreground access granted");
                    exclusiveAccessGranted = true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed exclusive foreground: {ex.Message}");
                    try
                    {
                        _wheel.SetCooperativeLevel(consoleWindow, CooperativeLevel.Exclusive | CooperativeLevel.Background);
                        Console.WriteLine("Exclusive background access granted");
                        exclusiveAccessGranted = true;
                    }
                    catch (Exception ex2)
                    {
                        Console.WriteLine($"Failed exclusive background: {ex2.Message}");
                        Console.WriteLine("   Could not get exclusive access - force feedback will be disabled");
                        Console.WriteLine("   To enable force feedback, try running as administrator");
                        
                        try
                        {
                            _wheel.SetCooperativeLevel(consoleWindow, CooperativeLevel.NonExclusive | CooperativeLevel.Background);
                            Console.WriteLine("Non-exclusive access granted (input only)");
                        }
                        catch (Exception ex3)
                        {
                            Console.WriteLine($"Failed to set any cooperative level: {ex3.Message}");
                            return false;
                        }
                    }
                }
                
                // Only try to disable auto-centering if we have exclusive access
                if (exclusiveAccessGranted)
                {
                    try
                    {
                        _wheel.Properties.AutoCenter = false;
                        Console.WriteLine("Auto-centering disabled");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Warning: Could not disable auto-centering: {ex.Message}");
                    }
                }
            }
            else
            {
                // For non-force feedback devices, non-exclusive is fine
                _wheel.SetCooperativeLevel(consoleWindow, CooperativeLevel.NonExclusive | CooperativeLevel.Background);
                Console.WriteLine("Non-exclusive access set for input-only device");
            }
            
            _wheel.Acquire();
            Console.WriteLine("Device acquired successfully");
            
            if (hasForceFeedback && exclusiveAccessGranted)
            {
                _forceFeedbackEnabled = InitializeForceFeedback();
                if (_forceFeedbackEnabled)
                {
                    Console.WriteLine("Force feedback initialized successfully!");
                }
                else
                {
                    Console.WriteLine("Force feedback initialization failed");
                }
            }
            else if (hasForceFeedback && !exclusiveAccessGranted)
            {
                Console.WriteLine("Force feedback skipped - exclusive access required");
            }
            
            Console.WriteLine($"Connection successful - Force feedback: {(_forceFeedbackEnabled ? "ENABLED" : "DISABLED")}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to connect to wheel: {ex.Message}");
            CleanupResources();
            return false;
        }
    }
    
    private bool InitializeForceFeedback()
    {
        if (_wheel == null) return false;
        
        try
        {
            Console.WriteLine("Initializing force feedback...");
            
            _numForceFeedbackAxes = 1;
            
            Console.WriteLine($"Found {_numForceFeedbackAxes} force feedback axes");
            
            if (_numForceFeedbackAxes == 0)
            {
                Console.WriteLine("No force feedback axes found");
                return false;
            }
            
            if (_numForceFeedbackAxes > 2)
                _numForceFeedbackAxes = 2;
            
            var effectParams = new EffectParameters
            {
                Flags = EffectFlags.Cartesian | EffectFlags.ObjectOffsets,
                Duration = int.MaxValue, // INFINITE
                SamplePeriod = 0,
                Gain = 10000, // DI_FFNOMINALMAX
                TriggerButton = -1, // DIEB_NOTRIGGER
                TriggerRepeatInterval = 0,
                StartDelay = 0
            };
            
            // Set up axes - use DIJOFS_X and DIJOFS_Y offsets
            var axes = new int[_numForceFeedbackAxes];
            var directions = new int[_numForceFeedbackAxes];
            
            axes[0] = 0; // DIJOFS_X equivalent
            directions[0] = 0;
            
            if (_numForceFeedbackAxes > 1)
            {
                axes[1] = 4; // DIJOFS_Y equivalent  
                directions[1] = 0;
            }
            
            effectParams.SetAxes(axes, directions);
            
            var constantForce = new ConstantForce { Magnitude = 0 };
            effectParams.Parameters = constantForce;
            
            _forceEffect = new Effect(_wheel, EffectGuid.ConstantForce, effectParams);
            
            _forceEffect.Start();
            
            Console.WriteLine("Constant force effect created and started");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Force feedback initialization failed: {ex.Message}");
            return false;
        }
    }
    
    public JoystickState? GetWheelState()
    {
        if (_wheel == null) return null;
        
        try
        {
            _wheel.Poll();
            return _wheel.GetCurrentState();
        }
        catch
        {
            return null;
        }
    }
    
    public void SetForceFeedback(float leftMotor, float rightMotor)
    {
        if (!_forceFeedbackEnabled || _wheel == null || _forceEffect == null) return;
        
        try
        {
            var xForce = (rightMotor - leftMotor) * 10000; // DI_FFNOMINALMAX
            var yForce = 0; // We only use X-axis for steering wheels
            
            xForce = Math.Max(-10000, Math.Min(10000, xForce));
            
            var effectParams = new EffectParameters
            {
                Flags = EffectFlags.Cartesian | EffectFlags.ObjectOffsets,
                StartDelay = 0
            };
            
            var directions = new int[_numForceFeedbackAxes];
            var constantForce = new ConstantForce();
            
            if (_numForceFeedbackAxes == 1)
            {
                // Single axis - apply force directly
                constantForce.Magnitude = (int)xForce;
                directions[0] = 0;
            }
            else
            {
                // Multiple axes - apply direction-based force
                directions[0] = (int)xForce;
                directions[1] = (int)yForce;
                constantForce.Magnitude = (int)Math.Sqrt(xForce * xForce + yForce * yForce);
            }
            
            effectParams.SetAxes(new int[_numForceFeedbackAxes], directions);
            effectParams.Parameters = constantForce;
            
            // Update the effect parameters and start immediately
            _forceEffect.SetParameters(effectParams, 
                EffectParameterFlags.Direction | 
                EffectParameterFlags.TypeSpecificParameters | 
                EffectParameterFlags.Start);
        }
        catch (Exception ex)
        {
            // Limit error spam
            if (DateTime.Now.Millisecond < 50)
            {
                Console.WriteLine($"Force feedback update failed: {ex.Message}");
            }
        }
    }
    
    private void CleanupResources()
    {
        try
        {
            if (_forceEffect != null)
            {
                _forceEffect.Stop();
                _forceEffect.Dispose();
                _forceEffect = null;
            }
        }
        catch { }
        
        try
        {
            if (_wheel != null)
            {
                _wheel.Unacquire();
                _wheel.Dispose();
                _wheel = null;
            }
        }
        catch { }
        
        _forceFeedbackEnabled = false;
        _numForceFeedbackAxes = 0;
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            CleanupResources();
            _directInput?.Dispose();
            _disposed = true;
        }
    }
}