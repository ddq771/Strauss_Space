# Development status

The project includes five engine models, component assembly, configurable tanks,
fixed and gimballed mounts, save/load, flight dynamics and telemetry.

The assembly panel switches to flight controls after launch. Arrow keys control
Pitch and Yaw; indicators show the command angles and return to center.
Impact handling stops the vehicle at the spherical planet surface and plays an
artistic fuel-scaled fireball with 64 temporary debris pieces.

Earlier automated Unity checks covered assembly, reload persistence and flight.
The latest scripts have been compiled by the local editor, but the new impact
effect, single-panel flow and keyboard gimbal indicators still need dedicated
Play Mode verification. Historical screenshots show earlier interface versions.

Physics remains a prototype: no aerodynamic drag, estimated component masses,
constant propellant densities, simplified atmosphere and approximate impact
geometry. The explosion and debris are visual effects, not blast physics.
