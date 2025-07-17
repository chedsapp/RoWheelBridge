# RoWheel Bridge

A .NET 9.0 application that bridges DirectInput steering wheel devices to virtual Xbox controllers with force feedback support.

## Usage

- Run the program as admin for force feedback to work correctly
- A calibration settings .Json file is created after first-time calibration. It will be referenced for later startups if present

## Requirements

- Windows 10/11
- DirectInput compatible steering wheel/pedals
- ViGEm Bus Driver (https://github.com/nefarius/ViGEmBus/releases)

## Default Input Mapping

Custom input mapping is planned to be added soon.

| Steering Wheel Input | Xbox Controller Output |
|---------------------|------------------------|
| Steering Wheel      | Left Thumbstick X      |
| Throttle Pedal      | Right Trigger          |
| Brake Pedal         | Left Trigger           |
| Clutch Pedal        | Right Thumbstick Y     |
| Shift Up Button     | Y Button               |
| Shift Down Button   | X Button               |

## Force Feedback

For force feedback to work in your game, send vibration calls through `HapticService`

- Xbox controller vibration from Roblox is converted to steering wheel force feedback
- **Small Motor** = Negative torque (left force)
- **Large Motor** = Positive torque (right force)