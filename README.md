# Frequency Response Automation

[![TAM - API](https://img.shields.io/static/v1?label=TAM&message=API&color=b51839)](https://www.triamec.com/en/tam-api.html)

Minimal example on how to use the TAM API to automate the frequency response measurement.

This application was built on the [Hello World! Example](https://github.com/Triamec/HelloWorld).

![TAM Frequency Response Automation](./doc/Screenshot.png)

## Hardware Prerequisites

To record a Frequency Response of an axis, you need a *Triamec* drive with a motor and encoder connected and configured with a stable position controller. Connect the drive by *Tria-Link*, *USB* or *Ethernet*.

## Software Prerequisites

This project is made and built with [Microsoft Visual Studio](https://visualstudio.microsoft.com/en/).

In addition you need [TAM Software](https://www.triamec.com/en/tam-software-support.html) installation.

## Run the *Hello World!* Application

1. Open the `Frequency Response Automation.sln`.
2. Open the `Frequency Response Automation.cs` (view code)
3. Set the name of the axis for `AxisName`. Double check it in the register *Axes[].Information.AxisName* using the *TAM System Explorer*.
4. Adjust the `Distance` constant to an appropriate value in the unit of the axis, considering the axis range of motion. The unit can be found at *Axes[].Parameters.PositionController.PositionUnit*
5. Disable offline mode with `readonly bool _offline = false;`.
6. Now make sure the *TAM System Explorer* is not connected to the drive, or simply close it.
7. Start the application.

## Operate the *Hello World!* Application

Press **Enable** to activate the axis.

```csharp
void EnableDrive() {

    // Set the drive operational, i.e. switch the power section on.
    _axis.Drive.SwitchOn();

    // Reset any axis error and enable the axis controller.
    _axis.Control(AxisControlCommands.ResetErrorAndEnable);
}
```

Press **Left** and **Right**. The motor moves the `Distance` value in the corresponding direction. Both buttons trigger the same method. Change the speed with the slider.

```csharp
void MoveAxis(int sign) =>

    // Move a distance with dedicated velocity.
    // If the axis is just moving, it is reprogrammed with this command.
    _axis.MoveRelative(Math.Sign(sign) * Distance, _velocityMaximum * _velocitySlider.Value * 0.01f);
```

Press **Disable** to switch off the axis.

```csharp
void DisableDrive() {

    // Disable the axis controller.
    _axis.Control(AxisControlCommands.Disable);

    // Switch the power section off.
    _axis.Drive.SwitchOff();
}
```

