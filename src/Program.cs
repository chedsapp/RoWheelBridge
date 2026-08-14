using RoWheelBridge;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using SharpDX.DirectInput;
using System.Runtime.InteropServices;

class Program
{
    public delegate bool ConsoleCtrlDelegate(int dwCtrlType);
    
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate HandlerRoutine, bool Add);
    
    private const int CTRL_C_EVENT = 0;
    private const int CTRL_BREAK_EVENT = 1;
    private const int CTRL_CLOSE_EVENT = 2;
    private const int CTRL_LOGOFF_EVENT = 5;
    private const int CTRL_SHUTDOWN_EVENT = 6;
    
    private static DirectInputManager? _inputManager;
    private static ViGEmClient? _vigemClient;
    private static IXbox360Controller? _controller;
    private static WheelCalibration _calibration = new();
    private static bool _running = true;
    private static ConsoleCtrlDelegate _consoleHandler = new ConsoleCtrlDelegate(ConsoleCtrlCheck);
    
    private static bool _debugMode = false;
    
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== RoWheel Bridge ===");
        Console.WriteLine("DirectInput Steering Wheel to Xbox Controller Bridge");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  RoWheelBridge.exe           - Normal operation");
        Console.WriteLine("  RoWheelBridge.exe --debug   - Enable debug output");
        Console.WriteLine("  RoWheelBridge.exe --list-devices - List all devices and exit");
        Console.WriteLine();
        
        // Check for debug flag
        _debugMode = args.Contains("--debug") || args.Contains("-d");
        if (_debugMode)
            Console.WriteLine("Debug mode enabled\n");
        
        // Check for device list flag
        if (args.Contains("--list-devices") || args.Contains("-l"))
        {
            ListAllDevicesAsync();
            return;
        }
        
        try
        {
            await InitializeAsync();
            await RunMainLoopAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
        finally
        {
            Cleanup();
        }
    }
    
    static async Task InitializeAsync()
    {
        // Console control handler to catch window close events
        SetConsoleCtrlHandler(_consoleHandler, true);
        
        _inputManager = new DirectInputManager();
        
        _vigemClient = new ViGEmClient();
        _controller = _vigemClient.CreateXbox360Controller();
        _controller.AutoSubmitReport = false;
        
        _controller.FeedbackReceived += OnFeedbackReceived;
        
        _controller.Connect();
        Console.WriteLine("Virtual Xbox controller connected");
        
        _calibration = WheelCalibration.LoadFromFile("calibration.json");
        
        await SelectWheelAsync();
        
        if (NeedsCalibration())
        {
            await RunCalibrationAsync();
        }
        
        Console.WriteLine("Setup complete! Press ESC to exit or close window.");
        Console.WriteLine();
    }
    
    static Task SelectWheelAsync()
    {
        try
        {
            var allDevices = _inputManager!.GetAllInputDevices(_debugMode);
            
            if (!allDevices.Any())
            {
                Console.WriteLine("ERROR: No input devices found!");
                ShowTroubleshootingInfo();
                throw new Exception("No input devices found!");
            }
            
            Console.WriteLine("\n=== DEVICE SETUP ===");
            Console.WriteLine("You can use the same device for multiple inputs, or separate devices.");
            Console.WriteLine("Available devices:");
            
            for (int i = 0; i < allDevices.Count; i++)
            {
                var deviceInfo = GetDeviceInfo(allDevices[i]);
                Console.WriteLine($"{i + 1}. {allDevices[i].ProductName} {deviceInfo}");
            }
            
            // Select steering wheel device
            Console.WriteLine("\n--- STEERING WHEEL SELECTION ---");
            Console.Write($"Select device for STEERING WHEEL (1-{allDevices.Count}): ");
            var steeringDevice = SelectDevice(allDevices);
            
            if (!_inputManager.ConnectToWheel(steeringDevice.InstanceGuid))
            {
                throw new Exception("Failed to connect to steering wheel device");
            }
            
            _calibration.SteeringDeviceGuid = steeringDevice.InstanceGuid.ToString();
            
            // Select pedal devices (can be same as steering or different)
            Console.WriteLine("\n--- PEDAL SELECTION ---");
            Console.WriteLine("Select device for pedals (can be same as steering wheel):");
            Console.Write($"Select device for PEDALS (1-{allDevices.Count}): ");
            var pedalDevice = SelectDevice(allDevices);
            
            if (pedalDevice.InstanceGuid != steeringDevice.InstanceGuid)
            {
                if (!_inputManager.ConnectToPedals(pedalDevice.InstanceGuid))
                {
                    Console.WriteLine("Warning: Failed to connect to separate pedal device, will use steering wheel device for pedals");
                    pedalDevice = steeringDevice;
                }
            }
            
            _calibration.ThrottleDeviceGuid = pedalDevice.InstanceGuid.ToString();
            _calibration.BrakeDeviceGuid = pedalDevice.InstanceGuid.ToString();
            _calibration.ClutchDeviceGuid = pedalDevice.InstanceGuid.ToString();
            
            // Select shifter device (optional)
            Console.WriteLine("\n--- SHIFTER SELECTION (OPTIONAL) ---");
            Console.Write("Do you have a separate shifter/button box? (y/N): ");
            string? shifterInput = Console.ReadLine();
            bool hasShifter = !string.IsNullOrEmpty(shifterInput) && 
                             (shifterInput.Trim().ToLower().StartsWith("y") || shifterInput.Trim().ToLower() == "yes");
            
            if (hasShifter)
            {
                Console.Write($"Select device for SHIFTER (1-{allDevices.Count}): ");
                var shifterDevice = SelectDevice(allDevices);
                
                if (shifterDevice.InstanceGuid != steeringDevice.InstanceGuid && 
                    shifterDevice.InstanceGuid != pedalDevice.InstanceGuid)
                {
                    if (!_inputManager.ConnectToShifter(shifterDevice.InstanceGuid))
                    {
                        Console.WriteLine("Warning: Failed to connect to separate shifter device, will use steering wheel device for shifter");
                        shifterDevice = steeringDevice;
                    }
                }
                
                _calibration.ShifterDeviceGuid = shifterDevice.InstanceGuid.ToString();
            }
            else
            {
                _calibration.ShifterDeviceGuid = steeringDevice.InstanceGuid.ToString();
            }
            
            Console.WriteLine("\nDevice setup complete!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during device selection: {ex.Message}");
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
            Environment.Exit(1);
        }
        
        return Task.CompletedTask;
    }
    

    
    static DeviceInstance SelectDevice(List<DeviceInstance> devices)
    {
        string? input = Console.ReadLine();
        
        if (string.IsNullOrEmpty(input))
        {
            throw new Exception("No input provided");
        }
        
        if (!int.TryParse(input.Trim(), out int choice))
        {
            throw new Exception($"Invalid input: '{input}' is not a number");
        }
        
        if (choice < 1 || choice > devices.Count)
        {
            throw new Exception($"Invalid choice: {choice} is out of range (1-{devices.Count})");
        }
        
        var selectedDevice = devices[choice - 1];
        Console.WriteLine($"Selected: {selectedDevice.ProductName}");
        Thread.Sleep(300);
        
        return selectedDevice;
    }
    
    static bool NeedsCalibration()
    {
        return _calibration.ThrottleAxis == -1 || 
               _calibration.BrakeAxis == -1 || 
               _calibration.SteeringAxis == -1 ||
               string.IsNullOrEmpty(_calibration.SteeringDeviceGuid);
    }
    
    static async Task RunCalibrationAsync()
    {
        Console.WriteLine("\n=== CALIBRATION PROCESS ===");
        Console.WriteLine("This will calibrate your steering wheel, pedals, and buttons.");
        Console.WriteLine("Press ENTER to continue...");
        Console.ReadLine();
        
        // Get device GUIDs
        var steeringGuid = Guid.Parse(_calibration.SteeringDeviceGuid);
        var pedalGuid = Guid.Parse(_calibration.ThrottleDeviceGuid);
        var shifterGuid = Guid.Parse(_calibration.ShifterDeviceGuid);
        
        // Calibrate throttle pedal
        await CalibratePedalAsync("THROTTLE", "throttle pedal", 
            (cal, axis, min, max) => { cal.ThrottleAxis = axis; cal.ThrottleMin = min; cal.ThrottleMax = max; }, pedalGuid);
        
        // Calibrate brake pedal
        await CalibratePedalAsync("BRAKE", "brake pedal", 
            (cal, axis, min, max) => { cal.BrakeAxis = axis; cal.BrakeMin = min; cal.BrakeMax = max; }, pedalGuid);
        
        // Calibrate clutch pedal (optional)
        await CalibrateClutchAsync(pedalGuid);
        
        // Calibrate steering wheel
        await CalibrateSteeringAsync(steeringGuid);
        
        // Calibrate shift buttons
        await CalibrateButtonsAsync(shifterGuid);
        
        // Save calibration
        _calibration.SaveToFile("calibration.json");
        Console.WriteLine("\nCalibration complete and saved!");
        Console.WriteLine("Press ENTER to continue...");
        Console.ReadLine();
    }
    
    static async Task CalibratePedalAsync(string pedalName, string description, 
        Action<WheelCalibration, int, int, int> setCalibration, Guid deviceGuid)
    {
        Console.WriteLine($"\n--- {pedalName} CALIBRATION ---");
        Console.WriteLine($"Press the {description} down ALL THE WAY and hold it.");
        Console.WriteLine("Press ENTER when ready...");
        Console.ReadLine();
        
        var maxState = await WaitForStableInput(deviceGuid);
        var maxValues = GetAxisValues(maxState);
        
        Console.WriteLine($"Now RELEASE the {description} completely.");
        Console.WriteLine("Press ENTER when ready...");
        Console.ReadLine();
        
        var minState = await WaitForStableInput(deviceGuid);
        var minValues = GetAxisValues(minState);
        
        // Find the axis with the biggest change
        int bestAxis = -1;
        int maxDifference = 0;
        
        for (int i = 0; i < Math.Min(maxValues.Length, minValues.Length); i++)
        {
            int difference = Math.Abs(maxValues[i] - minValues[i]);
            if (difference > maxDifference)
            {
                maxDifference = difference;
                bestAxis = i;
            }
        }
        
        if (bestAxis != -1 && maxDifference > 1000) // Minimum threshold
        {
            setCalibration(_calibration, bestAxis, minValues[bestAxis], maxValues[bestAxis]);
            Console.WriteLine($"{pedalName} calibrated: Axis {bestAxis}, Range {minValues[bestAxis]}-{maxValues[bestAxis]}");
        }
        else
        {
            Console.WriteLine($"Failed to detect {pedalName} movement. Please try again.");
            await CalibratePedalAsync(pedalName, description, setCalibration, deviceGuid);
        }
    }
    
    static async Task CalibrateClutchAsync(Guid deviceGuid)
    {
        Console.WriteLine("\n--- CLUTCH PEDAL CALIBRATION (OPTIONAL) ---");
        Console.WriteLine("Do you have a clutch pedal to calibrate?");
        Console.WriteLine("Many racing wheels only have throttle and brake pedals.");
        Console.Write("Calibrate clutch pedal? (y/N): ");
        
        string? input = Console.ReadLine();
        bool calibrateClutch = !string.IsNullOrEmpty(input) && 
                              (input.Trim().ToLower().StartsWith("y") || input.Trim().ToLower() == "yes");
        
        if (calibrateClutch)
        {
            await CalibratePedalAsync("CLUTCH", "clutch pedal", 
                (cal, axis, min, max) => { cal.ClutchAxis = axis; cal.ClutchMin = min; cal.ClutchMax = max; }, deviceGuid);
        }
        else
        {
            Console.WriteLine("Clutch pedal calibration skipped.");
            // Ensure clutch is disabled in calibration
            _calibration.ClutchAxis = -1;
            _calibration.ClutchMin = 0;
            _calibration.ClutchMax = 0;
        }
    }
    
    static async Task CalibrateSteeringAsync(Guid deviceGuid)
    {
        Console.WriteLine("\n--- STEERING WHEEL CALIBRATION ---");
        Console.WriteLine("Turn the steering wheel ALL THE WAY TO THE LEFT and hold it.");
        Console.WriteLine("Press ENTER when ready...");
        Console.ReadLine();
        
        var leftState = await WaitForStableInput(deviceGuid);
        var leftValues = GetAxisValues(leftState);
        
        Console.WriteLine("Now turn the steering wheel ALL THE WAY TO THE RIGHT and hold it.");
        Console.WriteLine("Press ENTER when ready...");
        Console.ReadLine();
        
        var rightState = await WaitForStableInput(deviceGuid);
        var rightValues = GetAxisValues(rightState);
        
        // Find the axis with the biggest change
        int bestAxis = -1;
        int maxDifference = 0;
        
        for (int i = 0; i < Math.Min(leftValues.Length, rightValues.Length); i++)
        {
            int difference = Math.Abs(leftValues[i] - rightValues[i]);
            if (difference > maxDifference)
            {
                maxDifference = difference;
                bestAxis = i;
            }
        }
        
        if (bestAxis != -1 && maxDifference > 1000)
        {
            _calibration.SteeringAxis = bestAxis;
            _calibration.SteeringMin = Math.Min(leftValues[bestAxis], rightValues[bestAxis]);
            _calibration.SteeringMax = Math.Max(leftValues[bestAxis], rightValues[bestAxis]);
            Console.WriteLine($"Steering calibrated: Axis {bestAxis}, Range {_calibration.SteeringMin}-{_calibration.SteeringMax}");
        }
        else
        {
            Console.WriteLine("Failed to detect steering movement. Please try again.");
            await CalibrateSteeringAsync(deviceGuid);
        }
    }
    
    static async Task CalibrateButtonsAsync(Guid deviceGuid)
    {
        Console.WriteLine("\n--- BUTTON CALIBRATION (OPTIONAL) ---");
        Console.WriteLine("Do you have paddle shifters or shift buttons to calibrate?");
        Console.Write("Calibrate shift buttons? (y/N): ");
        
        string? input = Console.ReadLine();
        bool calibrateButtons = !string.IsNullOrEmpty(input) && 
                               (input.Trim().ToLower().StartsWith("y") || input.Trim().ToLower() == "yes");
        
        if (calibrateButtons)
        {
            // Shift up button
            Console.WriteLine("\nPress and hold the SHIFT UP button (or paddle).");
            Console.WriteLine("Press ENTER when ready...");
            Console.ReadLine();
            
            var shiftUpState = await WaitForStableInput(deviceGuid);
            _calibration.ShiftUpButton = FindPressedButton(shiftUpState);
            
            if (_calibration.ShiftUpButton != -1)
            {
                Console.WriteLine($"Shift Up button detected: Button {_calibration.ShiftUpButton}");
            }
            else
            {
                Console.WriteLine("No button press detected for Shift Up.");
            }
            
            Console.WriteLine("Release the button and wait...");
            await Task.Delay(1000);
            
            // Shift down button
            Console.WriteLine("Press and hold the SHIFT DOWN button (or paddle).");
            Console.WriteLine("Press ENTER when ready...");
            Console.ReadLine();
            
            var shiftDownState = await WaitForStableInput(deviceGuid);
            _calibration.ShiftDownButton = FindPressedButton(shiftDownState);
            
            if (_calibration.ShiftDownButton != -1)
            {
                Console.WriteLine($"Shift Down button detected: Button {_calibration.ShiftDownButton}");
            }
            else
            {
                Console.WriteLine("No button press detected for Shift Down.");
            }
            // ERS button
            Console.WriteLine("\nPress and hold the ERS button.");
            Console.WriteLine("Press ENTER when ready...");
            Console.ReadLine();

            var ersState = await WaitForStableInput(deviceGuid);
            _calibration.ErsButton = FindPressedButton(ersState);

            if (_calibration.ErsButton != -1)
            {
                Console.WriteLine($"ERS button detected: Button {_calibration.ErsButton}");
            }
            else
            {
                Console.WriteLine("No button press detected for ERS.");
            }

            Console.WriteLine("Release the button and wait...");
            await Task.Delay(1000);
        }
        else
        {
            Console.WriteLine("Button calibration skipped.");
            // Ensure buttons are disabled in calibration
            _calibration.ShiftUpButton = -1;
            _calibration.ShiftDownButton = -1;
	        _calibration.ErsButton = -1;
        }
    }
    
    static async Task<JoystickState> WaitForStableInput(Guid deviceGuid)
    {
        JoystickState? state = null;
        int stableCount = 0;
        var lastState = _inputManager!.GetDeviceState(deviceGuid);
        
        while (stableCount < 10) // Wait for 10 stable readings
        {
            await Task.Delay(50);
            state = _inputManager.GetDeviceState(deviceGuid);
            
            if (state != null && AreStatesEqual(state, lastState))
            {
                stableCount++;
            }
            else
            {
                stableCount = 0;
            }
            
            lastState = state;
        }
        
        return state ?? new JoystickState();
    }
    
    static bool AreStatesEqual(JoystickState state1, JoystickState? state2)
    {
        if (state2 == null) return false;
        
        var values1 = GetAxisValues(state1);
        var values2 = GetAxisValues(state2);
        
        for (int i = 0; i < Math.Min(values1.Length, values2.Length); i++)
        {
            if (Math.Abs(values1[i] - values2[i]) > 100) // Allow small variations
                return false;
        }
        
        return true;
    }
    
    static int[] GetAxisValues(JoystickState state)
    {
        return new int[]
        {
            state.X, state.Y, state.Z,
            state.RotationX, state.RotationY, state.RotationZ,
            state.Sliders[0], state.Sliders[1]
        };
    }
    
    static int FindPressedButton(JoystickState state)
    {
        for (int i = 0; i < state.Buttons.Length; i++)
        {
            if (state.Buttons[i])
                return i;
        }
        return -1;
    }
    
    static string GetDeviceInfo(DeviceInstance device)
    {
        try
        {
            using var tempJoystick = new Joystick(_inputManager!.DirectInputInstance, device.InstanceGuid);
            var capabilities = tempJoystick.Capabilities;
            
            var info = new List<string>();
            
            if (capabilities.Flags.HasFlag(DeviceFlags.ForceFeedback))
                info.Add("Force Feedback");
                
            info.Add($"{capabilities.AxeCount} axes");
            info.Add($"{capabilities.ButtonCount} buttons");
            
            // Add device type info
            string typeInfo = device.Type.ToString();
            if (device.Type == DeviceType.Driving)
                typeInfo += " [WHEEL]";
            else if (device.Type == DeviceType.Joystick)
                typeInfo += " [JOYSTICK]";
            
            info.Add(typeInfo);
            
            return $"({string.Join(", ", info)})";
        }
        catch (Exception ex)
        {
            return $"(error: {ex.Message})";
        }
    }
    
    static void ShowTroubleshootingInfo()
    {
        Console.WriteLine("\n=== TROUBLESHOOTING INFORMATION ===");
        Console.WriteLine("If you're having issues finding your wheel device:");
        Console.WriteLine();
        Console.WriteLine("1. Make sure your wheel is:");
        Console.WriteLine("   - Properly connected via USB");
        Console.WriteLine("   - Powered on (if it has a power switch)");
        Console.WriteLine("   - Recognized by Windows (check Device Manager)");
        Console.WriteLine();
        Console.WriteLine("2. Try running this program as Administrator");
        Console.WriteLine("   - Right-click the executable and select 'Run as administrator'");
        Console.WriteLine("   - This is often required for force feedback to work");
        Console.WriteLine();
        Console.WriteLine("3. Check Windows Device Manager:");
        Console.WriteLine("   - Look under 'Human Interface Devices' or 'Sound, video and game controllers'");
        Console.WriteLine("   - Your wheel should appear without warning icons");
        Console.WriteLine();
        Console.WriteLine("4. If using debug mode (--debug flag):");
        Console.WriteLine("   - Look for detailed device information");
        Console.WriteLine("   - Check if multiple instances of the same device are found");
        Console.WriteLine();
        Console.WriteLine("Press ENTER to continue...");
        Console.ReadLine();
    }    

    static async Task RunMainLoopAsync()
    {
        Console.WriteLine("=== BRIDGE ACTIVE ===");
        Console.WriteLine("Steering wheel inputs are now mapped to Xbox controller.");
        Console.WriteLine("Press ESC to exit.");
        Console.WriteLine();
        
        while (_running)
        {
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Escape)
                {
                    _running = false;
                    break;
                }
            }
            
            UpdateXboxController();
            
            await Task.Delay(16); // ~60 FPS update rate
        }
    }
    
    static void UpdateXboxController()
    {
        if (_controller == null) return;
        
        // Get states from all connected devices
        var steeringState = _inputManager!.GetDeviceState(Guid.Parse(_calibration.SteeringDeviceGuid));
        var pedalState = _inputManager.GetDeviceState(Guid.Parse(_calibration.ThrottleDeviceGuid));
        var shifterState = _inputManager.GetDeviceState(Guid.Parse(_calibration.ShifterDeviceGuid));
        
        // Map steering wheel to left thumbstick X
        if (steeringState != null && _calibration.SteeringAxis >= 0)
        {
            var steeringValues = GetAxisValues(steeringState);
            if (_calibration.SteeringAxis < steeringValues.Length)
            {
                var steeringValue = steeringValues[_calibration.SteeringAxis];
                var normalizedSteering = NormalizeAxis(steeringValue, _calibration.SteeringMin, _calibration.SteeringMax);
                _controller.SetAxisValue(Xbox360Axis.LeftThumbX, normalizedSteering);
            }
        }
        
        // Map pedals
        if (pedalState != null)
        {
            var pedalValues = GetAxisValues(pedalState);
            
            // Map throttle to right trigger
            if (_calibration.ThrottleAxis >= 0 && _calibration.ThrottleAxis < pedalValues.Length)
            {
                var throttleValue = pedalValues[_calibration.ThrottleAxis];
                var normalizedThrottle = NormalizeAxis(throttleValue, _calibration.ThrottleMin, _calibration.ThrottleMax);
                _controller.SetSliderValue(Xbox360Slider.RightTrigger, (byte)(Math.Max(0, normalizedThrottle + 32768) / 256));
            }
            
            // Map brake to left trigger
            if (_calibration.BrakeAxis >= 0 && _calibration.BrakeAxis < pedalValues.Length)
            {
                var brakeValue = pedalValues[_calibration.BrakeAxis];
                var normalizedBrake = NormalizeAxis(brakeValue, _calibration.BrakeMin, _calibration.BrakeMax);
                _controller.SetSliderValue(Xbox360Slider.LeftTrigger, (byte)(Math.Max(0, normalizedBrake + 32768) / 256));
            }
            
            // Map clutch to left thumbstick Y (optional)
            if (_calibration.ClutchAxis >= 0 && _calibration.ClutchAxis < pedalValues.Length)
            {
                var clutchValue = pedalValues[_calibration.ClutchAxis];
                var normalizedClutch = NormalizeAxis(clutchValue, _calibration.ClutchMin, _calibration.ClutchMax);
                _controller.SetAxisValue(Xbox360Axis.LeftThumbY, normalizedClutch);
            }
        }
        
        // Map shift buttons
        if (shifterState != null)
        {
            if (_calibration.ShiftUpButton >= 0 && _calibration.ShiftUpButton < shifterState.Buttons.Length)
            {
                _controller.SetButtonState(Xbox360Button.Y, shifterState.Buttons[_calibration.ShiftUpButton]);
            }
            
            if (_calibration.ShiftDownButton >= 0 && _calibration.ShiftDownButton < shifterState.Buttons.Length)
            {
                _controller.SetButtonState(Xbox360Button.X, shifterState.Buttons[_calibration.ShiftDownButton]);
            }
            if (_calibration.ErsButton >= 0 && _calibration.ErsButton < shifterState.Buttons.Length)
            {       
                _controller.SetButtonState(Xbox360Button.B, shifterState.Buttons[_calibration.ErsButton]);
            }
        }
        
        _controller.SubmitReport();
        
        // Update display
        if (DateTime.Now.Millisecond % 20 < 16)
        {
            Console.SetCursorPosition(0, Console.CursorTop);
            
            var steeringDisplay = "N/A";
            var throttleDisplay = "N/A";
            var brakeDisplay = "N/A";
            var clutchDisplay = "N/A";
            
            if (steeringState != null && _calibration.SteeringAxis >= 0)
            {
                var steeringValues = GetAxisValues(steeringState);
                if (_calibration.SteeringAxis < steeringValues.Length)
                    steeringDisplay = steeringValues[_calibration.SteeringAxis].ToString().PadLeft(6);
            }
            
            if (pedalState != null)
            {
                var pedalValues = GetAxisValues(pedalState);
                if (_calibration.ThrottleAxis >= 0 && _calibration.ThrottleAxis < pedalValues.Length)
                    throttleDisplay = pedalValues[_calibration.ThrottleAxis].ToString().PadLeft(6);
                if (_calibration.BrakeAxis >= 0 && _calibration.BrakeAxis < pedalValues.Length)
                    brakeDisplay = pedalValues[_calibration.BrakeAxis].ToString().PadLeft(6);
                if (_calibration.ClutchAxis >= 0 && _calibration.ClutchAxis < pedalValues.Length)
                    clutchDisplay = pedalValues[_calibration.ClutchAxis].ToString().PadLeft(6);
            }
            
            Console.Write($"Steering: {steeringDisplay} | Throttle: {throttleDisplay} | Brake: {brakeDisplay} | Clutch: {clutchDisplay}    ");
        }
    }
    
    static short NormalizeAxis(int value, int min, int max)
    {
        if (max == min) return 0;
        
        // Normalize to -32768 to 32767 range
        var normalized = (float)(value - min) / (max - min); // 0.0 to 1.0
        normalized = (normalized * 2.0f) - 1.0f; // -1.0 to 1.0
        return (short)(normalized * 32767);
    }
    
    static void OnFeedbackReceived(object sender, Xbox360FeedbackReceivedEventArgs e)
    {
        // Convert Xbox controller vibration to force feedback
        // Small motor (high frequency) = negative torque
        // Large motor (low frequency) = positive torque
        var leftMotor = e.SmallMotor / 255.0f;  // Normalize to 0.0-1.0
        var rightMotor = e.LargeMotor / 255.0f; // Normalize to 0.0-1.0
        
        _inputManager?.SetForceFeedback(leftMotor, rightMotor);
    }
    
    static bool ConsoleCtrlCheck(int ctrlType)
    {
        // Handle console control events (window close, Ctrl+C, etc.)
        switch (ctrlType)
        {
            case CTRL_C_EVENT:
            case CTRL_BREAK_EVENT:
            case CTRL_CLOSE_EVENT:
            case CTRL_LOGOFF_EVENT:
            case CTRL_SHUTDOWN_EVENT:
                Console.WriteLine("\nReceived close signal, cleaning up...");
                _running = false;
                Cleanup();
                return true; // Indicate we handled the event
        }
        return false;
    }
    
    static void ListAllDevicesAsync()
    {
        Console.WriteLine("=== DEVICE DISCOVERY TOOL ===");
        Console.WriteLine("This tool lists all available input devices for troubleshooting.\n");
        
        try
        {
            _inputManager = new DirectInputManager();
            
            Console.WriteLine("Enumerating devices with detailed debugging...");
            var allDevices = _inputManager.GetAllInputDevices(true);
            
            if (!allDevices.Any())
            {
                Console.WriteLine("\nERROR: No input devices found!");
                Console.WriteLine("This could indicate:");
                Console.WriteLine("  - No compatible devices are connected");
                Console.WriteLine("  - DirectInput drivers are not working properly");
                Console.WriteLine("  - Windows needs to be restarted after driver installation");
                Console.WriteLine("  - The application needs to be run as administrator");
                ShowTroubleshootingInfo();
                return;
            }
            
            Console.WriteLine("\n=== DETAILED DEVICE INFORMATION ===");
            
            for (int i = 0; i < allDevices.Count; i++)
            {
                var device = allDevices[i];
                Console.WriteLine($"\n--- Device {i + 1}: {device.ProductName} ---");
                Console.WriteLine($"  Instance GUID: {device.InstanceGuid}");
                Console.WriteLine($"  Product GUID: {device.ProductGuid}");
                Console.WriteLine($"  Device Type: {device.Type}");
                Console.WriteLine($"  Usage: {device.Usage} (Page: {device.UsagePage})");
                
                try
                {
                    using var tempJoystick = new Joystick(_inputManager.DirectInputInstance, device.InstanceGuid);
                    var capabilities = tempJoystick.Capabilities;
                    
                    Console.WriteLine($"  Capabilities:");
                    Console.WriteLine($"    Axes: {capabilities.AxeCount}");
                    Console.WriteLine($"    Buttons: {capabilities.ButtonCount}");
                    Console.WriteLine($"    POV Hats: {capabilities.PovCount}");
                    Console.WriteLine($"    Force Feedback: {(capabilities.Flags.HasFlag(DeviceFlags.ForceFeedback) ? "Yes" : "No")}");
                    Console.WriteLine($"    Driver Version: {capabilities.DriverVersion}");
                    Console.WriteLine($"    Firmware Revision: {capabilities.FirmwareRevision}");
                    Console.WriteLine($"    Hardware Revision: {capabilities.HardwareRevision}");
                    
                    // Test basic connection
                    try
                    {
                        tempJoystick.SetCooperativeLevel(IntPtr.Zero, CooperativeLevel.NonExclusive | CooperativeLevel.Background);
                        tempJoystick.Acquire();
                        var state = tempJoystick.GetCurrentState();
                        Console.WriteLine($"  Connection Test: SUCCESS");
                        Console.WriteLine($"    Current State - X: {state.X}, Y: {state.Y}, Z: {state.Z}");
                        Console.WriteLine($"    Active Buttons: {state.Buttons.Count(b => b)}");
                        
                        // Test if we can read multiple states (device responsiveness)
                        Thread.Sleep(50);
                        tempJoystick.Poll();
                        var state2 = tempJoystick.GetCurrentState();
                        bool responsive = state.X != state2.X || state.Y != state2.Y || 
                                        state.Buttons.Zip(state2.Buttons, (a, b) => a != b).Any(changed => changed);
                        Console.WriteLine($"    Device Responsiveness: {(responsive ? "ACTIVE (values changing)" : "STABLE (no movement detected)")}");
                        
                        tempJoystick.Unacquire();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  Connection Test: FAILED");
                        Console.WriteLine($"    Error: {ex.Message}");
                        Console.WriteLine($"    This device may not be usable");
                    }
                    
                    // Test force feedback capability if available
                    if (capabilities.Flags.HasFlag(DeviceFlags.ForceFeedback))
                    {
                        try
                        {
                            tempJoystick.SetCooperativeLevel(IntPtr.Zero, CooperativeLevel.Exclusive | CooperativeLevel.Background);
                            tempJoystick.Acquire();
                            Console.WriteLine($"  Force Feedback Test: Can acquire exclusive access");
                            tempJoystick.Unacquire();
                        }
                        catch
                        {
                            Console.WriteLine($"  Force Feedback Test: Cannot get exclusive access (may need admin rights)");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ERROR: Cannot examine device capabilities");
                    Console.WriteLine($"    {ex.Message}");
                    Console.WriteLine($"    This device is likely not compatible or has driver issues");
                }
            }
            
            // Show device suitability analysis
            Console.WriteLine("\n=== DEVICE SUITABILITY ANALYSIS ===");
            
            var suitableDevices = allDevices.Where(d => 
            {
                try
                {
                    using var tempJoystick = new Joystick(_inputManager.DirectInputInstance, d.InstanceGuid);
                    var capabilities = tempJoystick.Capabilities;
                    return capabilities.AxeCount >= 1 && (capabilities.ButtonCount > 0 || capabilities.Flags.HasFlag(DeviceFlags.ForceFeedback));
                }
                catch
                {
                    return false;
                }
            }).ToList();
            
            if (suitableDevices.Any())
            {
                Console.WriteLine($"Found {suitableDevices.Count} device(s) that appear suitable for wheel/controller use:");
                foreach (var device in suitableDevices.OrderByDescending(d => 
                {
                    try
                    {
                        using var tempJoystick = new Joystick(_inputManager.DirectInputInstance, d.InstanceGuid);
                        var capabilities = tempJoystick.Capabilities;
                        int score = 0;
                        if (capabilities.Flags.HasFlag(DeviceFlags.ForceFeedback)) score += 100;
                        if (d.Type == DeviceType.Driving) score += 50;
                        score += capabilities.AxeCount * 10 + capabilities.ButtonCount * 2;
                        return score;
                    }
                    catch { return 0; }
                }))
                {
                    var deviceInfo = GetDeviceInfo(device);
                    Console.WriteLine($"  ✓ {device.ProductName} {deviceInfo}");
                }
                Console.WriteLine("\nAny of these devices can be selected manually during setup.");
            }
            else
            {
                Console.WriteLine("No devices appear suitable for wheel/controller use.");
                Console.WriteLine("This could indicate driver issues or incompatible hardware.");
            }
            
            Console.WriteLine("\n=== RECOMMENDATIONS ===");
            
            var hasHidDevices = allDevices.Any(d => d.ProductName.ToLowerInvariant().Contains("hid"));
            var hasJoystickDevices = allDevices.Any(d => d.Type == DeviceType.Joystick);
            var hasForceFeeback = allDevices.Any(d => 
            {
                try
                {
                    using var tempJoystick = new Joystick(_inputManager.DirectInputInstance, d.InstanceGuid);
                    return tempJoystick.Capabilities.Flags.HasFlag(DeviceFlags.ForceFeedback);
                }
                catch { return false; }
            });
            var hasConnectionIssues = allDevices.Any(d => GetDeviceInfo(d).Contains("Connection Failed"));
            
            if (hasHidDevices)
            {
                Console.WriteLine("• Some devices show as 'HID' - consider installing manufacturer-specific drivers for better functionality");
            }
            if (hasJoystickDevices)
            {
                Console.WriteLine("• Joystick-type devices found - these can work as wheels if they have multiple axes");
            }
            if (hasForceFeeback)
            {
                Console.WriteLine("• Force feedback devices detected - run as administrator to enable force feedback");
            }
            if (hasConnectionIssues)
            {
                Console.WriteLine("• Some devices have connection issues - try running as administrator or updating drivers");
            }
            if (allDevices.Count < 2)
            {
                Console.WriteLine("• Very few devices detected - check that your wheel is connected and powered on");
            }
            if (!suitableDevices.Any())
            {
                Console.WriteLine("• No suitable devices found - check device manager for driver issues or hardware problems");
            }
            
            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"CRITICAL ERROR during device discovery: {ex.Message}");
            Console.WriteLine("This indicates a serious DirectInput or system issue.");
            Console.WriteLine("Try running as administrator or reinstalling DirectX.");
        }
        finally
        {
            _inputManager?.Dispose();
        }
    }
    
    static void Cleanup()
    {
        Console.WriteLine("\nShutting down...");
        
        _running = false;
        _controller?.Disconnect();
        _vigemClient?.Dispose();
        _inputManager?.Dispose();
        
        Console.WriteLine("Cleanup complete.");
    }
}