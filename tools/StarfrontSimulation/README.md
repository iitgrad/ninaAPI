# Starfront Simulation camera

Test fixture for the isolated Parallels NINA environment, separate from the Starfront Control plugin. **Do not install on the observatory NUC.**

The equipment provider adds **Starfront Thermal Simulator Camera**, ID `d038dcf9-969f-4bd4-b215-2fd61c182242`. It subclasses NINA's built-in simulator, retaining its IMAGE/DIRECTORY feed and setup, and explicitly reimplements the camera interface's thermal properties. It never enumerates or connects physical hardware. The existing NINA simulator's image settings are reused; they remain separate from production profiles.

The sensor starts at15°C with cooling off and a−10°C set point. Cooling/warming follows a20-second first-order response; max delta is40°C. Simulated cooler load varies with temperature error and settles according to the requested delta. Supported set points are−40…30°C; impossible colder targets saturate at−25°C. This is a visual/control test model, not a physical camera calibration. Reads advance a monotonic-clock model with no worker timer, log or retained history.

Build with `dotnet build tools/StarfrontSimulation -c Release`. Install only StarfrontSimulation.dll in the test user's NINA Plugins/3.0.0/Starfront Simulation folder while NINA is closed. Select the new camera in the simulation profile, and add its exact ID to that VM's Starfront Control allowedDeviceIds. Keep simulationOnly=true. The core test project links the platform-independent thermal model and tests cooling, warm-up, read-frequency independence and invalid/unreachable targets.

The camera uses NINA's existing exposure and image processing pipeline; telemetry, UI and commands remain the real plugin paths. To revert, disconnect and select NINA's original simulator camera, then close NINA and remove this fixture folder. No Starfront Control or NINA binaries are replaced.
