# Rocket assembly prototype

Open **Strauss Space → Open Rocket Assembly**, then enter Play Mode. The scene is PlanetScene, with the assembly site on Earth. **Earth / Planet** switches to a globe view.

- **Engines**: select F-1, RD-180, RS-25, Merlin 1D or Raptor 2, then click an empty numbered attachment or install into the selected slot. Click an engine, tank or frame directly to select it; the selected component changes color. Press **Delete** to remove it and **Undo change** to restore it. Delete does not remove components while a text field has focus.
- **Frames**: install a fixed or gimballed component, with 1, 3, 5 or 7 mounts. Each mount has its own angle. The preview limit is 10 degrees, not a claimed specification of any real engine. Frame spacing includes clearance for the maximum angle.
- **Tanks**: independent fuel and oxidizer components. Set capacity in cubic metres and diameter in metres, then apply. The simplified flat-ended cylindrical vessel height is V / (π D² / 4). Removing a tank or changing its height repositions the component above it. Capacity is not remaining liquid quantity.
- Save/load stores the component layout, dimensions, engine choices and angles in rocket-assembly.json under Unity's persistent data directory. Save explicitly before leaving Play Mode. Undo restores component changes. Reducing mount count preserves dormant engine selections for later expansion.

Engine-specific thrust, separate fuel/oxidizer consumption, component mass and gimbal torque now feed flight physics. Launch controls and formulas are available in the assembly menu. Reference/estimated data and current approximations are documented in ../Docs/EngineParameters.md. Aerodynamic drag and structural loads are not simulated. Engine assets are visual approximations. Cylinder input bounds are displayed when values exceed usable mesh/camera limits.

Validation in Unity 6000.3.22f1 Play Mode: independent tank resizing; adjoining attachment positions; mixed seven-engine cluster with all five models; individual gimbal and fixed mount constraints; engine/frame/tank removal and undo; save/load; Earth camera regression check. Passed. A separate Unity SearchDatabase startup exception was logged by the editor; component checks completed successfully.
