using RoWheelBridge;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using SharpDX.DirectInput;

class Program
{
    private static DirectInputManager? _inputManager;
    private static ViGEmClient? _vigemClient;
    private static IXbox360Controller? _controller;
    private static WheelCalibration _calibration = new();
    private static bool _running = true;
    
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== RoWheel Bridge ===");
        Console.WriteLine("DirectInput Steering Wheel to Xbox Controller Bridge");
        Console.WriteLine();
        
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
        // Initialize DirectInput
        _inputManager = new DirectInputManager();
        
        // Initialize ViGEm (Xbox controller emulation)
        _vigemClient = new ViGEmClient();
        _controller = _vigemClient.CreateXbox360Controller();
        _controller.AutoSubmitReport = false;
        
        // Set up force feedback callback
        _controller.FeedbackReceived += OnFeedbackReceived;
        
        // Connect controller
        _controller.Connect();
        Console.WriteLine("Virtual Xbox controller connected");
        
        // Load or create calibration
        _calibration = WheelCalibration.LoadFromFile("calibration.json");
        
        // Select and connect to wheel
        await SelectWheelAsync();
        
        // Run calibration if needed
        if (NeedsCalibration())
        {
            await RunCalibrationAsync();
        }
        
        Console.WriteLine("Setup complete! Press ESC to exit.");
        Console.WriteLine();
    }
    
    static Task SelectWheelAsync()
    {
        try
        {
            var devices = _inputManager!.GetWheelDevices();
            
            if (!devices.Any())
            {
                throw new Exception("No steering wheel devices found!");
            }
            
            Console.WriteLine("Available steering wheel devices:");
            for (int i = 0; i < devices.Count; i++)
            {
                Console.WriteLine($"{i + 1}. {devices[i].ProductName}");
            }
            
            Console.Write("Select device (1-" + devices.Count + "): ");
            string? input = Console.ReadLine();
            
            // Safer parsing with better error handling
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
            Console.WriteLine($"Connecting to: {selectedDevice.ProductName}...");
            
            // Add a small delay to ensure UI updates before potentially intensive operations
            Thread.Sleep(500);
            
            if (_inputManager.ConnectToWheel(selectedDevice.InstanceGuid))
            {
                Console.WriteLine($"Connected to: {selectedDevice.ProductName}");
            }
            else
            {
                throw new Exception("Failed to connect to selected device");
            }
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
    
    static bool NeedsCalibration()
    {
        return _calibration.ThrottleAxis == -1 || 
               _calibration.BrakeAxis == -1 || 
               _calibration.SteeringAxis == -1;
    }
    
    static async Task RunCalibrationAsync()
    {
        Console.WriteLine("\n=== CALIBRATION PROCESS ===");
        Console.WriteLine("This will calibrate your steering wheel, pedals, and buttons.");
        Console.WriteLine("Press ENTER to continue...");
        Console.ReadLine();
        
        // Calibrate throttle pedal
        await CalibratePedalAsync("THROTTLE", "throttle pedal", 
            (cal, axis, min, max) => { cal.ThrottleAxis = axis; cal.ThrottleMin = min; cal.ThrottleMax = max; });
        
        // Calibrate brake pedal
        await CalibratePedalAsync("BRAKE", "brake pedal", 
            (cal, axis, min, max) => { cal.BrakeAxis = axis; cal.BrakeMin = min; cal.BrakeMax = max; });
        
        // Calibrate clutch pedal
        await CalibratePedalAsync("CLUTCH", "clutch pedal", 
            (cal, axis, min, max) => { cal.ClutchAxis = axis; cal.ClutchMin = min; cal.ClutchMax = max; });
        
        // Calibrate steering wheel
        await CalibrateSteeringAsync();
        
        // Calibrate shift buttons
        await CalibrateButtonsAsync();
        
        // Save calibration
        _calibration.SaveToFile("calibration.json");
        Console.WriteLine("\nCalibration complete and saved!");
        Console.WriteLine("Press ENTER to continue...");
        Console.ReadLine();
    }
    
    static async Task CalibratePedalAsync(string pedalName, string description, 
        Action<WheelCalibration, int, int, int> setCalibration)
    {
        Console.WriteLine($"\n--- {pedalName} CALIBRATION ---");
        Console.WriteLine($"Press the {description} down ALL THE WAY and hold it.");
        Console.WriteLine("Press ENTER when ready...");
        Console.ReadLine();
        
        var maxState = await WaitForStableInput();
        var maxValues = GetAxisValues(maxState);
        
        Console.WriteLine($"Now RELEASE the {description} completely.");
        Console.WriteLine("Press ENTER when ready...");
        Console.ReadLine();
        
        var minState = await WaitForStableInput();
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
            await CalibratePedalAsync(pedalName, description, setCalibration);
        }
    }
    
    static async Task CalibrateSteeringAsync()
    {
        Console.WriteLine("\n--- STEERING WHEEL CALIBRATION ---");
        Console.WriteLine("Turn the steering wheel ALL THE WAY TO THE LEFT and hold it.");
        Console.WriteLine("Press ENTER when ready...");
        Console.ReadLine();
        
        var leftState = await WaitForStableInput();
        var leftValues = GetAxisValues(leftState);
        
        Console.WriteLine("Now turn the steering wheel ALL THE WAY TO THE RIGHT and hold it.");
        Console.WriteLine("Press ENTER when ready...");
        Console.ReadLine();
        
        var rightState = await WaitForStableInput();
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
            await CalibrateSteeringAsync();
        }
    }
    
    static async Task CalibrateButtonsAsync()
    {
        Console.WriteLine("\n--- BUTTON CALIBRATION ---");
        
        // Shift up button
        Console.WriteLine("Press and hold the SHIFT UP button.");
        Console.WriteLine("Press ENTER when ready...");
        Console.ReadLine();
        
        var shiftUpState = await WaitForStableInput();
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
        Console.WriteLine("Press and hold the SHIFT DOWN button.");
        Console.WriteLine("Press ENTER when ready...");
        Console.ReadLine();
        
        var shiftDownState = await WaitForStableInput();
        _calibration.ShiftDownButton = FindPressedButton(shiftDownState);
        
        if (_calibration.ShiftDownButton != -1)
        {
            Console.WriteLine($"Shift Down button detected: Button {_calibration.ShiftDownButton}");
        }
        else
        {
            Console.WriteLine("No button press detected for Shift Down.");
        }
    }
    
    static async Task<JoystickState> WaitForStableInput()
    {
        JoystickState? state = null;
        int stableCount = 0;
        var lastState = _inputManager!.GetWheelState();
        
        while (stableCount < 10) // Wait for 10 stable readings
        {
            await Task.Delay(50);
            state = _inputManager.GetWheelState();
            
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
            
            var state = _inputManager!.GetWheelState();
            if (state != null)
            {
                UpdateXboxController(state);
            }
            
            await Task.Delay(16); // ~60 FPS update rate
        }
    }
    
    static void UpdateXboxController(JoystickState wheelState)
    {
        if (_controller == null) return;
        
        var axisValues = GetAxisValues(wheelState);
        
        // Map steering wheel to left thumbstick X
        if (_calibration.SteeringAxis >= 0 && _calibration.SteeringAxis < axisValues.Length)
        {
            var steeringValue = axisValues[_calibration.SteeringAxis];
            var normalizedSteering = NormalizeAxis(steeringValue, _calibration.SteeringMin, _calibration.SteeringMax);
            _controller.SetAxisValue(Xbox360Axis.LeftThumbX, normalizedSteering);
        }
        
        // Map throttle to right trigger
        if (_calibration.ThrottleAxis >= 0 && _calibration.ThrottleAxis < axisValues.Length)
        {
            var throttleValue = axisValues[_calibration.ThrottleAxis];
            var normalizedThrottle = NormalizeAxis(throttleValue, _calibration.ThrottleMin, _calibration.ThrottleMax);
            _controller.SetSliderValue(Xbox360Slider.RightTrigger, (byte)(Math.Max(0, normalizedThrottle + 32768) / 256)); // Convert to 0-255 range
        }
        
        // Map brake to left trigger
        if (_calibration.BrakeAxis >= 0 && _calibration.BrakeAxis < axisValues.Length)
        {
            var brakeValue = axisValues[_calibration.BrakeAxis];
            var normalizedBrake = NormalizeAxis(brakeValue, _calibration.BrakeMin, _calibration.BrakeMax);
            _controller.SetSliderValue(Xbox360Slider.LeftTrigger, (byte)(Math.Max(0, normalizedBrake + 32768) / 256)); // Convert to 0-255 range
        }
        
        // Map clutch to right thumbstick Y (optional)
        if (_calibration.ClutchAxis >= 0 && _calibration.ClutchAxis < axisValues.Length)
        {
            var clutchValue = axisValues[_calibration.ClutchAxis];
            var normalizedClutch = NormalizeAxis(clutchValue, _calibration.ClutchMin, _calibration.ClutchMax);
            _controller.SetAxisValue(Xbox360Axis.LeftThumbY, normalizedClutch);
        }
        
        // Map shift buttons
        if (_calibration.ShiftUpButton >= 0 && _calibration.ShiftUpButton < wheelState.Buttons.Length)
        {
            _controller.SetButtonState(Xbox360Button.Y, wheelState.Buttons[_calibration.ShiftUpButton]);
        }
        
        if (_calibration.ShiftDownButton >= 0 && _calibration.ShiftDownButton < wheelState.Buttons.Length)
        {
            _controller.SetButtonState(Xbox360Button.X, wheelState.Buttons[_calibration.ShiftDownButton]);
        }
        
        // Submit the report
        _controller.SubmitReport();
        
        // Display current values (optional debug info)
        if (DateTime.Now.Millisecond % 20 < 16) // Update display every ~20ms
        {
            Console.SetCursorPosition(0, Console.CursorTop);
            Console.Write($"Steering: {(_calibration.SteeringAxis >= 0 ? axisValues[_calibration.SteeringAxis].ToString().PadLeft(6) : "N/A")} | " +
                         $"Throttle: {(_calibration.ThrottleAxis >= 0 ? axisValues[_calibration.ThrottleAxis].ToString().PadLeft(6) : "N/A")} | " +
                         $"Brake: {(_calibration.BrakeAxis >= 0 ? axisValues[_calibration.BrakeAxis].ToString().PadLeft(6) : "N/A")}    ");
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