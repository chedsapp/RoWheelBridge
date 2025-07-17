using SharpDX.DirectInput;
using System.Runtime.InteropServices;

namespace RoWheelBridge;

public class DirectInputManager : IDisposable
{
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();
    
    private DirectInput _directInput;
    private Joystick? _wheel;
    private Effect? _forceEffect;
    private bool _disposed = false;
    private bool _forceFeedbackEnabled = false;
    private int _numForceFeedbackAxes = 0;
    
    public DirectInputManager()
    {
        _directInput = new DirectInput();
    }
    
    public List<DeviceInstance> GetWheelDevices()
    {
        return _directInput.GetDevices(DeviceType.Driving, DeviceEnumerationFlags.AllDevices)
            .Concat(_directInput.GetDevices(DeviceType.Joystick, DeviceEnumerationFlags.AllDevices))
            .Where(device => device.Type != DeviceType.Mouse && device.Type != DeviceType.Keyboard)
            .ToList();
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
            
            if (hasForceFeedback)
            {
                var consoleWindow = GetConsoleWindow();
                if (consoleWindow == IntPtr.Zero)
                {
                    Console.WriteLine("Warning: Could not get console window handle");
                    consoleWindow = IntPtr.Zero; // Use desktop window
                }
                
                // Set cooperative level
                try
                {
                    _wheel.SetCooperativeLevel(consoleWindow, CooperativeLevel.Exclusive | CooperativeLevel.Foreground);
                    Console.WriteLine("Set exclusive foreground access");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to set exclusive foreground: {ex.Message}");
                    try
                    {
                        _wheel.SetCooperativeLevel(consoleWindow, CooperativeLevel.Exclusive | CooperativeLevel.Background);
                        Console.WriteLine("Set exclusive background access");
                    }
                    catch (Exception ex2)
                    {
                        Console.WriteLine($"Failed to set exclusive access: {ex2.Message}");
                        return false; // Force feedback requires exclusive access
                    }
                }
                
                // Disable auto-centering spring
                try
                {
                    _wheel.Properties.AutoCenter = false;
                    Console.WriteLine("Disabled auto-centering");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Could not disable auto-centering: {ex.Message}");
                }
            }
            else
            {
                // For non-force feedback devices, non-exclusive is fine
                var consoleWindow = GetConsoleWindow();
                _wheel.SetCooperativeLevel(consoleWindow, CooperativeLevel.NonExclusive | CooperativeLevel.Background);
                Console.WriteLine("Set non-exclusive access for input-only device");
            }
            
            _wheel.Acquire();
            Console.WriteLine("Device acquired successfully");
            
            if (hasForceFeedback)
            {
                _forceFeedbackEnabled = InitializeForceFeedback();
                if (_forceFeedbackEnabled)
                {
                    Console.WriteLine("✓ Force feedback initialized successfully!");
                }
                else
                {
                    Console.WriteLine("✗ Force feedback initialization failed");
                }
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