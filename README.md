# RoWheel Bridge

A .NET 9.0 application that bridges DirectInput steering wheel devices to virtual Xbox controllers with force feedback support.

## Usage

- Run the program as admin for force feedback to work correctly
- A calibration settings .Json file is created after first-time calibration. It will be referenced for later startups if present
- Use `--debug` or `-d` flag to see detailed device detection information

### Multi-Device Support

RoWheel Bridge supports both **combined** and **separate** device setups:

- **Combined Setup**: Use a single device (like a Logitech G29) for steering, pedals, and shifters
- **Separate Setup**: Use different devices for each component (e.g., separate wheelbase, pedal set, and shifter)
- **Mixed Setup**: Any combination of the above (e.g., combined wheel+pedals with separate shifter)

During device selection, you'll be prompted to choose:
1. **Steering Wheel Device** - The device with your steering wheel (usually has force feedback)
2. **Pedal Device** - Can be the same as steering wheel or a separate pedal set
3. **Shifter Device** - Optional separate shifter/button box, or use steering wheel device

### Device Detection

The program detects and categorizes devices automatically:

- **Wheelbase**: Devices with force feedback and multiple axes (steering wheels)
- **Pedals**: Devices with multiple axes but fewer buttons (dedicated pedal sets)
- **Shifter/Buttons**: Devices with many buttons but fewer axes (button boxes, shifters)
- **Multi-axis**: General devices that don't fit other categories

Use the `--debug` flag to see detailed device information and categorization.

### Calibration Options

During calibration, you can skip optional components:

- **Clutch Pedal**: Many setups only have throttle and brake pedals
- **Shift Buttons**: Skip if you don't have paddle shifters or shift buttons

The program works perfectly with just steering wheel, throttle, and brake pedals calibrated.

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