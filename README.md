# RoWheel Bridge

A .NET 9.0 application that bridges DirectInput steering wheel devices to virtual Xbox controllers with force feedback support.

## Usage

- Run the program as admin for force feedback to work correctly
- A calibration settings .Json file is created after first-time calibration. It will be referenced for later startups if present
- Use `--debug` or `-d` flag to see detailed device detection information

### Device Selection

The program now automatically filters duplicate devices (common with racing wheel setups that show the same wheel multiple times for different components like pedals, shifter, etc.). It prioritizes:

1. Devices with force feedback capabilities
2. Devices with multiple axes and buttons (main wheel unit)
3. Devices specifically tagged as driving/joystick types

If you're still seeing multiple identical devices, try running with the `--debug` flag to see what's being detected and filtered.

### Calibration Options

During the calibration process, you can now skip optional components:

- **Clutch Pedal**: Many racing wheels only have throttle and brake pedals. You'll be asked if you want to calibrate a clutch pedal.
- **Shift Buttons**: If you don't have paddle shifters or shift buttons, you can skip this calibration step.

The program will work perfectly fine with just steering wheel, throttle, and brake pedals calibrated.

## Requirements

- Windows 10/11
- DirectInput compatible steering wheel/pedals
- ViGEm Bus Driver (https://github.com/nefarius/ViGEmBus/releases)

## Default Input Mapping

Custom input mapping is planned to be added soon.

| Steering Wheel Input | Xbox Controller Output | Required |
|---------------------|------------------------|----------|
| Steering Wheel      | Left Thumbstick X      | Yes      |
| Throttle Pedal      | Right Trigger          | Yes      |
| Brake Pedal         | Left Trigger           | Yes      |
| Clutch Pedal        | Left Thumbstick Y      | No       |
| Shift Up Button     | Y Button               | No       |
| Shift Down Button   | X Button               | No       |

## Force Feedback Implementation

For force feedback to work in your game, send vibration calls through `HapticService`

- Xbox controller vibration from Roblox is converted to steering wheel force feedback
- **Small Motor** = Negative torque (left force)
- **Large Motor** = Positive torque (right force)