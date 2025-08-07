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
    private Joystick? _pedals;
    private Joystick? _shifter;
    private Effect? _forceEffect;
    private bool _disposed = false;
    private bool _forceFeedbackEnabled = false;
    private int _numForceFeedbackAxes = 0;
    
    public DirectInputManager()
    {
        _directInput = new DirectInput();
    }
    
    public List<DeviceInstance> GetAllInputDevices(bool debugOutput = false)
    {
        try
        {
            if (debugOutput)
                Console.WriteLine("Starting device enumeration...");
            
            var allDevices = _directInput.GetDevices(DeviceClass.All, DeviceEnumerationFlags.AllDevices)
                .Where(device => device.Type != DeviceType.Mouse && 
                               device.Type != DeviceType.Keyboard)
                .ToList();
            
            if (debugOutput)
                Console.WriteLine($"Found {allDevices.Count} non-mouse/keyboard devices");
            
            // Also try GameControl class specifically for better wheel detection
            try
            {
                var gameDevices = _directInput.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AllDevices)
                    .Where(device => device.Type != DeviceType.Mouse && 
                                   device.Type != DeviceType.Keyboard)
                    .ToList();
                
                if (debugOutput)
                    Console.WriteLine($"Found {gameDevices.Count} additional game control devices");
                
                // Merge with existing devices, avoiding duplicates by GUID
                var existingGuids = allDevices.Select(d => d.InstanceGuid).ToHashSet();
                var newDevices = gameDevices.Where(d => !existingGuids.Contains(d.InstanceGuid)).ToList();
                allDevices.AddRange(newDevices);
                
                if (debugOutput && newDevices.Any())
                    Console.WriteLine($"Added {newDevices.Count} unique game control devices");
            }
            catch (Exception ex)
            {
                if (debugOutput)
                    Console.WriteLine($"Warning: Could not enumerate GameControl devices: {ex.Message}");
            }
            
            if (!allDevices.Any())
            {
                if (debugOutput)
                    Console.WriteLine("No devices found at all - this might indicate a DirectInput issue");
                return new List<DeviceInstance>();
            }
            
            // Group by name and select the best instance of each device
            var deviceGroups = allDevices.GroupBy(d => d.ProductName).ToList();
            var filteredDevices = new List<DeviceInstance>();
            var duplicateCount = 0;
            
            foreach (var group in deviceGroups)
            {
                var devices = group.ToList();
                
                if (devices.Count > 1)
                {
                    duplicateCount += devices.Count - 1;
                    if (debugOutput)
                        Console.WriteLine($"  Found {devices.Count} instances of: {group.Key}");
                    
                    // Prioritize devices with more capabilities and working connections
                    var bestDevice = devices.OrderByDescending(d => GetDeviceCapabilityScore(d, debugOutput))
                                           .First();
                    filteredDevices.Add(bestDevice);
                    
                    if (debugOutput)
                    {
                        foreach (var device in devices)
                        {
                            var deviceType = GetDeviceTypeDescription(device);
                            var score = GetDeviceCapabilityScore(device, false);
                            var selected = device == bestDevice ? " [SELECTED]" : "";
                            Console.WriteLine($"    Instance: {device.InstanceGuid} ({deviceType}) Score: {score}{selected}");
                        }
                    }
                }
                else
                {
                    filteredDevices.Add(devices[0]);
                    if (debugOutput)
                    {
                        var deviceType = GetDeviceTypeDescription(devices[0]);
                        Console.WriteLine($"  Found: {devices[0].ProductName} ({deviceType})");
                    }
                }
            }
            
            if (debugOutput)
                Console.WriteLine($"Filtered {duplicateCount} duplicates, returning {filteredDevices.Count} unique devices");
            
            return filteredDevices;
        }
        catch (Exception ex)
        {
            if (debugOutput)
                Console.WriteLine($"Error during device enumeration: {ex.Message}");
            return new List<DeviceInstance>();
        }
    }
    

    
    private int GetDeviceCapabilityScore(DeviceInstance device, bool debugOutput = false)
    {
        try
        {
            using var tempJoystick = new Joystick(_directInput, device.InstanceGuid);
            var capabilities = tempJoystick.Capabilities;
            
            int score = 0;
            
            // Force feedback devices get highest priority (likely main wheel)
            if (capabilities.Flags.HasFlag(DeviceFlags.ForceFeedback))
                score += 1000;
            
            // Driving type devices get high priority
            if (device.Type == DeviceType.Driving)
                score += 500;
            
            // Check for common wheel device names (case insensitive)
            string productName = device.ProductName.ToLowerInvariant();
            bool hasWheelName = productName.Contains("wheel") || 
                               productName.Contains("g25") || productName.Contains("g27") || productName.Contains("g29") || 
                               productName.Contains("g920") || productName.Contains("g923") || productName.Contains("g935") ||
                               productName.Contains("t150") || productName.Contains("t300") || productName.Contains("t500") || 
                               productName.Contains("tmx") || productName.Contains("t248") ||
                               productName.Contains("csl") || productName.Contains("clubsport") || productName.Contains("podium") ||
                               productName.Contains("driving") || productName.Contains("racing") ||
                               productName.Contains("fanatec") || productName.Contains("thrustmaster") || productName.Contains("logitech");
            
            if (hasWheelName)
                score += 300; // Bonus for wheel-like names
            
            // More axes generally means more capable device
            score += capabilities.AxeCount * 15;
            
            // More buttons can indicate a more complete device
            score += capabilities.ButtonCount * 3;
            
            // POV hats add some value
            score += capabilities.PovCount * 8;
            
            // Test if we can actually connect to the device and read state
            bool canConnect = false;
            bool hasValidState = false;
            try
            {
                tempJoystick.SetCooperativeLevel(IntPtr.Zero, CooperativeLevel.NonExclusive | CooperativeLevel.Background);
                tempJoystick.Acquire();
                var state = tempJoystick.GetCurrentState();
                canConnect = true;
                
                // Check if the device provides meaningful data
                var axisValues = new int[] { state.X, state.Y, state.Z, state.RotationX, state.RotationY, state.RotationZ };
                hasValidState = axisValues.Any(v => v != 0) || state.Buttons.Any(b => b);
                
                tempJoystick.Unacquire();
                
                score += 100; // Bonus for working connection
                if (hasValidState)
                    score += 50; // Extra bonus for devices that provide data
            }
            catch
            {
                score -= 200; // Heavy penalty for connection issues
            }
            
            // Penalize devices with no capabilities
            if (capabilities.AxeCount == 0 && capabilities.ButtonCount == 0)
                score -= 500;
            
            if (debugOutput)
            {
                Console.WriteLine($"      Score calculation:");
                Console.WriteLine($"        Force Feedback: {capabilities.Flags.HasFlag(DeviceFlags.ForceFeedback)} (+{(capabilities.Flags.HasFlag(DeviceFlags.ForceFeedback) ? 1000 : 0)})");
                Console.WriteLine($"        Device Type: {device.Type} (+{(device.Type == DeviceType.Driving ? 500 : 0)})");
                Console.WriteLine($"        Wheel Name: {hasWheelName} (+{(hasWheelName ? 300 : 0)})");
                Console.WriteLine($"        Axes: {capabilities.AxeCount} (+{capabilities.AxeCount * 15})");
                Console.WriteLine($"        Buttons: {capabilities.ButtonCount} (+{capabilities.ButtonCount * 3})");
                Console.WriteLine($"        Connection: {canConnect} (+{(canConnect ? 100 : -200)})");
                Console.WriteLine($"        Total Score: {score}");
            }
            
            return score;
        }
        catch (Exception ex)
        {
            if (debugOutput)
                Console.WriteLine($"      Error calculating score: {ex.Message}");
            return -500; // Heavily penalize devices we can't examine
        }
    }
    
    private string GetDeviceTypeDescription(DeviceInstance device)
    {
        try
        {
            using var tempJoystick = new Joystick(_directInput, device.InstanceGuid);
            var capabilities = tempJoystick.Capabilities;
            
            var features = new List<string>();
            
            // Connection status
            bool canConnect = false;
            try
            {
                tempJoystick.SetCooperativeLevel(IntPtr.Zero, CooperativeLevel.NonExclusive | CooperativeLevel.Background);
                tempJoystick.Acquire();
                var state = tempJoystick.GetCurrentState();
                canConnect = true;
                tempJoystick.Unacquire();
            }
            catch
            {
                // Connection failed
            }
            
            // Basic capabilities
            if (capabilities.Flags.HasFlag(DeviceFlags.ForceFeedback))
                features.Add("Force Feedback");
            
            features.Add($"{capabilities.AxeCount} axes");
            features.Add($"{capabilities.ButtonCount} buttons");
            
            if (capabilities.PovCount > 0)
                features.Add($"{capabilities.PovCount} POV");
            
            // Connection status
            features.Add(canConnect ? "Working" : "Connection Failed");
            
            // Try to categorize device type based on name and capabilities
            string productName = device.ProductName.ToLowerInvariant();
            string category = "Unknown";
            
            // Check for wheel-specific names first
            if (productName.Contains("g25") || productName.Contains("g27") || productName.Contains("g29") || 
                productName.Contains("g920") || productName.Contains("g923") || productName.Contains("g935"))
                category = "Logitech Wheel";
            else if (productName.Contains("t150") || productName.Contains("t300") || productName.Contains("t500") || 
                     productName.Contains("tmx") || productName.Contains("t248"))
                category = "Thrustmaster Wheel";
            else if (productName.Contains("csl") || productName.Contains("clubsport") || productName.Contains("podium"))
                category = "Fanatec Wheel";
            else if (productName.Contains("wheel") || productName.Contains("driving") || productName.Contains("racing"))
                category = "Racing Wheel";
            else if (device.Type == DeviceType.Driving)
                category = "Driving Device";
            else if (capabilities.Flags.HasFlag(DeviceFlags.ForceFeedback) && capabilities.AxeCount >= 2)
                category = "Force Feedback Device";
            else if (capabilities.AxeCount >= 3 && capabilities.ButtonCount <= 6)
                category = "Pedal Set";
            else if (capabilities.ButtonCount >= 8 && capabilities.AxeCount <= 2)
                category = "Button Box/Shifter";
            else if (capabilities.AxeCount >= 2)
                category = "Multi-axis Controller";
            else if (capabilities.ButtonCount > 0)
                category = "Button Controller";
            
            return $"{category}: {string.Join(", ", features)}";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }
    
    public bool ConnectToWheel(Guid deviceGuid)
    {
        try
        {
            Console.WriteLine("Connecting to steering wheel...");
            
            CleanupWheelResources();
            
            _wheel = new Joystick(_directInput, deviceGuid);
            Console.WriteLine($"Wheel Device: {_wheel.Information.ProductName}");
            
            // Check capabilities with detailed reporting
            var capabilities = _wheel.Capabilities;
            bool hasForceFeedback = capabilities.Flags.HasFlag(DeviceFlags.ForceFeedback);
            Console.WriteLine($"Device Capabilities:");
            Console.WriteLine($"  Axes: {capabilities.AxeCount}");
            Console.WriteLine($"  Buttons: {capabilities.ButtonCount}");
            Console.WriteLine($"  POV Hats: {capabilities.PovCount}");
            Console.WriteLine($"  Force Feedback: {(hasForceFeedback ? "Yes" : "No")}");
            
            // Verify we have at least basic capabilities
            if (capabilities.AxeCount == 0)
            {
                Console.WriteLine("WARNING: Device reports 0 axes - this may indicate a driver issue");
                Console.WriteLine("Try updating your wheel drivers or running as administrator");
            }
            
            if (capabilities.ButtonCount == 0)
            {
                Console.WriteLine("WARNING: Device reports 0 buttons - this may indicate a driver issue");
            }
            
            var consoleWindow = GetConsoleWindow();
            if (consoleWindow == IntPtr.Zero)
            {
                Console.WriteLine("Warning: Could not get console window handle, using desktop window");
                consoleWindow = IntPtr.Zero;
            }
            
            bool exclusiveAccessGranted = false;
            
            if (hasForceFeedback)
            {
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
                            Console.WriteLine("This usually indicates a driver or permission issue");
                            return false;
                        }
                    }
                }
                
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
                try
                {
                    _wheel.SetCooperativeLevel(consoleWindow, CooperativeLevel.NonExclusive | CooperativeLevel.Background);
                    Console.WriteLine("Non-exclusive access set for input-only device");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to set cooperative level: {ex.Message}");
                    Console.WriteLine("This usually indicates a driver issue");
                    return false;
                }
            }
            
            try
            {
                _wheel.Acquire();
                Console.WriteLine("Wheel device acquired successfully");
                
                // Test reading the device state
                var testState = _wheel.GetCurrentState();
                Console.WriteLine($"Initial state test - X: {testState.X}, Buttons: {testState.Buttons.Count(b => b)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to acquire device: {ex.Message}");
                Console.WriteLine("This may indicate the device is in use by another application");
                return false;
            }
            
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
            
            Console.WriteLine($"Wheel connection successful - Force feedback: {(_forceFeedbackEnabled ? "ENABLED" : "DISABLED")}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to connect to wheel: {ex.Message}");
            Console.WriteLine("Common causes:");
            Console.WriteLine("  - Device drivers not installed or outdated");
            Console.WriteLine("  - Device in use by another application");
            Console.WriteLine("  - Insufficient permissions (try running as administrator)");
            Console.WriteLine("  - Device not properly connected or powered on");
            CleanupWheelResources();
            return false;
        }
    }
    
    public bool ConnectToPedals(Guid deviceGuid)
    {
        try
        {
            Console.WriteLine("Connecting to pedals...");
            
            CleanupPedalResources();
            
            _pedals = new Joystick(_directInput, deviceGuid);
            Console.WriteLine($"Pedal Device: {_pedals.Information.ProductName}");
            
            var consoleWindow = GetConsoleWindow();
            if (consoleWindow == IntPtr.Zero)
                consoleWindow = IntPtr.Zero;
            
            _pedals.SetCooperativeLevel(consoleWindow, CooperativeLevel.NonExclusive | CooperativeLevel.Background);
            _pedals.Acquire();
            
            Console.WriteLine("Pedal device acquired successfully");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to connect to pedals: {ex.Message}");
            CleanupPedalResources();
            return false;
        }
    }
    
    public bool ConnectToShifter(Guid deviceGuid)
    {
        try
        {
            Console.WriteLine("Connecting to shifter...");
            
            CleanupShifterResources();
            
            _shifter = new Joystick(_directInput, deviceGuid);
            Console.WriteLine($"Shifter Device: {_shifter.Information.ProductName}");
            
            var consoleWindow = GetConsoleWindow();
            if (consoleWindow == IntPtr.Zero)
                consoleWindow = IntPtr.Zero;
            
            _shifter.SetCooperativeLevel(consoleWindow, CooperativeLevel.NonExclusive | CooperativeLevel.Background);
            _shifter.Acquire();
            
            Console.WriteLine("Shifter device acquired successfully");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to connect to shifter: {ex.Message}");
            CleanupShifterResources();
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
        catch (Exception ex)
        {
            // Device might have been disconnected or lost
            if (ex.Message.Contains("DIERR_INPUTLOST") || ex.Message.Contains("DIERR_NOTACQUIRED"))
            {
                try
                {
                    _wheel.Acquire();
                    _wheel.Poll();
                    return _wheel.GetCurrentState();
                }
                catch
                {
                    // Still failing, device likely disconnected
                    return null;
                }
            }
            return null;
        }
    }
    
    public JoystickState? GetPedalState()
    {
        if (_pedals == null) return null;
        
        try
        {
            _pedals.Poll();
            return _pedals.GetCurrentState();
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("DIERR_INPUTLOST") || ex.Message.Contains("DIERR_NOTACQUIRED"))
            {
                try
                {
                    _pedals.Acquire();
                    _pedals.Poll();
                    return _pedals.GetCurrentState();
                }
                catch
                {
                    return null;
                }
            }
            return null;
        }
    }
    
    public JoystickState? GetShifterState()
    {
        if (_shifter == null) return null;
        
        try
        {
            _shifter.Poll();
            return _shifter.GetCurrentState();
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("DIERR_INPUTLOST") || ex.Message.Contains("DIERR_NOTACQUIRED"))
            {
                try
                {
                    _shifter.Acquire();
                    _shifter.Poll();
                    return _shifter.GetCurrentState();
                }
                catch
                {
                    return null;
                }
            }
            return null;
        }
    }
    
    public JoystickState? GetDeviceState(Guid deviceGuid)
    {
        if (_wheel?.Information.InstanceGuid == deviceGuid)
            return GetWheelState();
        if (_pedals?.Information.InstanceGuid == deviceGuid)
            return GetPedalState();
        if (_shifter?.Information.InstanceGuid == deviceGuid)
            return GetShifterState();
        
        return null;
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
    
    private void CleanupWheelResources()
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
    
    private void CleanupPedalResources()
    {
        try
        {
            if (_pedals != null)
            {
                _pedals.Unacquire();
                _pedals.Dispose();
                _pedals = null;
            }
        }
        catch { }
    }
    
    private void CleanupShifterResources()
    {
        try
        {
            if (_shifter != null)
            {
                _shifter.Unacquire();
                _shifter.Dispose();
                _shifter = null;
            }
        }
        catch { }
    }
    
    private void CleanupResources()
    {
        CleanupWheelResources();
        CleanupPedalResources();
        CleanupShifterResources();
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