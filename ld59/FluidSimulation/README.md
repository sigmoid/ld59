# GPU-Based Fluid Simulation

This is a GPU-accelerated fluid simulation implemented using MonoGame and the Quartz engine. The simulation uses a grid-based Eulerian approach implementing Jos Stam's "Stable Fluids" algorithm.

## Implementation Details

The fluid simulation consists of the following components:

1. **FluidSimulator.cs**: Core class that handles the fluid simulation logic using GPU acceleration.
2. **FluidEffect.fx**: HLSL shader that performs the fluid calculations on the GPU.
3. **FluidSimulationScene.cs**: Scene class that integrates the fluid simulation with the game.

## How It Works

The simulation uses a grid-based approach to solve the Navier-Stokes equations for incompressible fluids. The main steps in the simulation are:

1. **Diffusion**: Spreading of velocity and density over time.
2. **Pressure Calculation**: Computing pressure from velocity divergence.
3. **Projection**: Making the velocity field mass-conserving (divergence-free).
4. **Advection**: Moving quantities through the velocity field.

All these steps are performed on the GPU using render targets and HLSL shaders for maximum performance.

## User Interaction

- **Left Mouse Button**: Click and drag to add density and force to the fluid.
- The direction and speed of your mouse movement determine the force applied to the fluid.

## Performance Considerations

- The grid size can be adjusted to balance between visual quality and performance.
- The simulation parameters (diffusion, viscosity, etc.) can be tuned for different fluid behaviors.

## Future Improvements

- Add more user controls for adjusting simulation parameters in real-time.
- Implement multiple fluid types with different properties.
- Add obstacles and boundaries for the fluid to interact with.
- Optimize the shader code for better performance.
