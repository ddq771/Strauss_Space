# Engine parameters and derived performance

Design specification for F-1, RD-180, RS-25, Merlin 1D Sea-level and Raptor 2 Sea-level. The core mass, thrust, pressure correction, propellant consumption and force/torque calculations are connected to Unity flight physics. Additional fields such as restart limits and detailed burn-duration qualification remain specification items.

## Model scope

Use an editable, empirical engine model: measured reference performance plus nozzle exit geometry. These are independent inputs to this simplified model, not independent physical design variables in a real engine. Changing nozzle diameter alone does not predict a redesigned engine's vacuum performance. Such a prediction requires a separate thermodynamic model.

The simulator UI is entirely English. Each engine card has **Base Parameters**, **Operating Conditions**, **Calculated Results**, and **Show Formula**. Editing a preset creates an **Experimental** instance; **Reset to Reference** restores the sourced values.

## Base Parameters — editable for every engine

| Field | Unit / type | Meaning |
|---|---|---|
| Engine Variant | text | Exact engine revision and source reference operating point |
| Optimization | Sea-level / Vacuum | Intended application; a label, not an additional efficiency multiplier |
| Dry Mass | kg | Engine mass; record whether actuators or mounting hardware are included |
| Reference Vacuum Thrust | N | Total engine vacuum thrust at the defined reference setting |
| Reference Vacuum Specific Impulse | s | Vacuum Isp at that same setting |
| Nozzles | list | Each nozzle has its own exit diameter, attachment location, thrust share and direction |
| Nozzle Exit Diameter | m per nozzle | Inner flow exit diameter, not overall engine width or mesh bounds |
| Fuel | propellant identifier | Fuel supplied by the fuel tank |
| Oxidizer | propellant identifier | Oxidizer supplied by the oxidizer tank |
| Mixture Ratio (O/F) | kg/kg | Oxidizer mass flow divided by fuel mass flow |
| Minimum / Maximum Throttle | fraction | Valid operating range, normalized to the reference point |
| Gimbal Compatibility | Fixed-only / Compatible / Unknown | Mounting capability; physical mounting fit is checked separately |
| Engine Gimbal Limit | degrees, where known | Engine/feed-system motion limit; the frame has separate actuator limits |
| Maximum Burn Duration | s or Unknown | Operating limit, not a burn time computed from tank quantity |
| Restart Capability | supported / unsupported / Unknown | If supported, record start-count and ignition constraints separately |
| Source / Data Status | metadata per value | Published, Estimated, User-defined or Unknown; include version and operating conditions |

For a multi-nozzle engine, total thrust and total mass are entered once. The sum of nozzle thrust shares is one. Changing nozzle count is a configuration change, not a way to multiply the stated engine thrust automatically. Chamber count must not be multiplied into engine mass either.

**Expansion Ratio**, **Chamber Pressure**, and engine cycle can be retained as reference metadata. They are not additional independent inputs to the simple pressure correction below. In a later thermodynamic model, throat area, chamber state and gas properties would determine performance, replacing independently entered vacuum thrust/Isp where appropriate.

## Operating Conditions — supplied by the experiment

- **Ambient Pressure**, Pa: atmosphere model or experiment override. Use absolute pressure; vacuum is zero.
- **Throttle Command**, dimensionless, and **Engine State**: commands rather than fixed engine characteristics.
- **Fuel Remaining / Oxidizer Remaining**, kg: tank state. Convert volume to mass using propellant density at the selected temperature/pressure, stored with the propellant model.
- **Mount Orientation / Actuator State**: supplied by the selected frame.
- **Vehicle Mass / Center of Mass / Local Gravity**: assembly and environment state.

The frame supplies its own mass, actuator angle/rate limits and attachment geometry. A physically compatible fixed-only engine can be installed on a moving frame with its rotation locked. Unknown compatibility must not silently become a confirmed physical capability.

## Calculated Results — read-only

Use SI units internally. Standard gravity g0 = 9.80665 m/s² is a fixed conversion constant, not the local planet's gravity. In the following equations, q is the actual operating fraction, p is ambient pressure, Fv0 and Iv0 are the reference vacuum thrust and Isp, and mE is engine dry mass.

| Result | Formula | Conditions |
|---|---|---|
| Total Nozzle Exit Area | A = Σ(π D_i² / 4) | Sum the areas of all flow exits |
| Reference Propellant Mass Flow | mdot0 = Fv0 / (g0 Iv0) | Both reference values must describe the same operating point |
| Vacuum Thrust | Fv(q) = q Fv0 | Explicit linear-throttle approximation |
| Propellant Mass Flow | mdot(q) = q mdot0 | Constant vacuum-Isp approximation |
| Ambient Pressure Thrust Loss | ΔF = p A | Fixed nozzle, same internal operating state, attached flow |
| Current Thrust | F(q,p) = q Fv0 − p A | Only for an operating engine within the model's valid domain |
| Current Specific Impulse | Isp(q,p) = F(q,p) / (g0 mdot(q)) | Requires positive mass flow |
| Effective Exhaust Velocity | veff = g0 Isp | Includes the pressure-thrust contribution; not nozzle gas velocity alone |
| Engine Thrust-to-Weight Ratio | TWRengine = F / (mE g0) | Engine only; separate from vehicle TWR |
| Fuel Mass Flow | mdotF = mdot / (1 + O/F) | O/F is a mass ratio |
| Oxidizer Mass Flow | mdotO = mdot (O/F) / (1 + O/F) | mdotF + mdotO = mdot |
| Fuel / Oxidizer Volume Flow | VdotF = mdotF / rhoF; VdotO = mdotO / rhoO | Densities come from the propellant state |
| Remaining Burn Time | t = min(mFuel / mdotF, mOxidizer / mdotO) | Constant operating conditions; cap by remaining rated duration if known |
| Thrust Force Vector | Fvector = Σ(F_i direction_i) | Directions come from the actual nozzle/mount poses |
| Thrust Torque | torque = Σ((position_i − COM) × Fvector_i) | Application positions are measured relative to vehicle center of mass |
| Accumulated Total Impulse | J = integral of F(t) dt | N·s; separate from specific impulse |
| Thrust Acceleration | avector = Fvector / Mvehicle | Add gravity and aerodynamic forces separately for net acceleration |

For independently directed nozzles, apply the pressure correction to each nozzle before summing vectors. A scalar total area is sufficient only for the corresponding scalar sum/parallel-nozzle case. The general thrust relation and definition of Isp are documented by [NASA: Rocket Thrust](https://www1.grc.nasa.gov/beginners-guide-to-aeronautics/rocket-thrust/) and [NASA: Specific Impulse](https://www1.grc.nasa.gov/beginners-guide-to-aeronautics/specific-impulse/).

### Validity and feedback

- When the engine is off, thrust and flow are zero; current Isp and remaining burn time display **N/A**. Do not evaluate the pressure correction as negative engine thrust at zero throttle.
- A nonpositive predicted thrust, missing required input, or a setting outside a validated pressure/throttle range displays **Outside Model Range** or **Missing Input**, not a plausible-looking result.
- Flow separation, startup/shutdown transients, mixture-dependent combustion and nonlinear throttle behavior are not predicted by this model. Positive calculated thrust alone does not establish safe or realistic operation.
- Published sea-level thrust and Isp are **Reference Checks**, not extra independent values forced to agree with the vacuum/diameter inputs. Display their residual against the model prediction. Rounded manufacturer data can disagree slightly.
- Editing fuel identity or O/F does not automatically predict new combustion efficiency. Mark that combination **Experimental**; the entered Isp remains a calibration assumption.
- **Show Formula** displays the equation, substituted values with units, result, assumptions and data status. Unknown inputs yield unknown dependent outputs; never substitute zero.

## Reference cards for the five current assets

Every card uses the entire Base Parameters table above. The values below are a research seed, not a fully qualified engine database. Unfilled fields remain **Unknown** until supported by a consistent source/configuration. The current 3D meshes are visual approximations and are not engineering measurement sources.

| Parameter | F-1 | RD-180 | RS-25 | Merlin 1D | Raptor 2 |
|---|---|---|---|---|---|
| Selected profile | NASA nominal F-1 reference | Glavkosmos published RD-180 | SLS, 109% rated power reference | Sea-level version | Sea-level version |
| Optimization | Sea-level | Sea-level | Sea-level launch, operates through ascent | Sea-level | Sea-level |
| Modeled nozzle count | 1 | 2 | 1 | 1 | 1 |
| Fuel / Oxidizer | RP-1 / LOX | Kerosene / LOX | LH2 / LOX | RP-1 / LOX | Methane / LOX |
| Reference vacuum thrust | 7,776.4 kN | 4,148.2 kN | 2,278.8 kN | Unknown in this research seed | Unknown in this research seed |
| Reference vacuum Isp | 304.8 s | 339 s | 452 s | Unknown | Unknown |
| Published engine mass | 8,444.1 kg; scope to confirm | Dry: 5,480 kg | 3,526.7 kg; hardware scope to confirm | Unknown | Unknown |
| O/F mass ratio | 2.270 | Unknown in selected primary source | 6.0 | Unknown | 3.6 only as an Estimated research assumption |
| Flow exit diameter | Unknown; do not use overall width | Unknown per nozzle; do not use 3.2 m overall width | Unknown; do not use 96 in overall width | Unknown | 1.3 m as an Estimated research input |
| Published sea-level thrust check | 6,770.2 kN | 3,824.6 kN | 1,859.3 kN | 845 kN on SpaceX vehicle page | About 2,260 kN in the DLR baseline; not independently verified here against original SpaceX announcement |
| Published sea-level Isp check | 265.4 s | 311 s | 366 s | Unknown | Unknown |
| Throttle limits | Unknown in selected sheet | 47–100% | Reference is 109% historical RPL; separate limits still to qualify | Unknown | Unknown |

The RS-25 reference is defined as q = 1 in our model even though its source calls it 109% of historical rated power. Do not multiply its quoted thrust by 1.09 again.

### F-1 source and derived preview

[NASA F-1 Propulsion System](https://ntrs.nasa.gov/api/citations/19930019136/downloads/19930019136.pdf) gives nominal vacuum thrust 1,748,200 lbf, sea-level thrust 1,522,008 lbf, vacuum Isp 304.8 s, sea-level Isp 265.4 s, mixture ratio 2.270 and engine weight 18,616 lbm. Using this reference yields **Propellant Mass Flow ≈ 2,601.6 kg/s** and **Vacuum Engine TWR ≈ 93.9** with the quoted engine mass. Hardware inclusion must be retained as a qualification note.

### RD-180 source and derived preview

[Glavkosmos RD-180](https://trade.glavkosmos.com/rd-180/) gives 390/423 metric ton-force sea-level/vacuum thrust, 311/339 s Isp, 5,480 kg dry mass and a 47–100% thrust range. This reference yields **Propellant Mass Flow ≈ 1,247.8 kg/s** and **Vacuum Engine TWR ≈ 77.2**. The entire two-nozzle engine is one engine entry.

### RS-25 source and derived preview

[L3Harris RS-25 specifications](https://www.l3harris.com/all-capabilities/rs-25-engine) give 512,300 lbf vacuum thrust, 418,000 lbf sea-level thrust, 452/366 s Isp, O/F = 6.0 and weight 7,775 lb, at the stated 109% reference where applicable. This yields **Propellant Mass Flow ≈ 514.1 kg/s** and **Vacuum Engine TWR ≈ 65.9** with that mass convention. Other RS-25 revisions require separate presets.

### Merlin 1D source and qualification

[SpaceX Falcon 9 / Merlin](https://new.spacex.com/vehicles/falcon-9) lists the sea-level Merlin at 845 kN with RP-1/LOX. Its 981 kN figure belongs to Merlin Vacuum and must not be used as the vacuum performance of the sea-level engine. The [Falcon User's Guide](https://www.spacex.com/assets/media/falcon-users-guide-2025-05-09.pdf) uses stage-level figures and a distinct vacuum second-stage engine. Mass, Isp and nozzle-flow diameter remain unfilled here rather than mixing revisions or using unverified common internet values. Required derived outputs consequently remain **Missing Input**.

### Raptor 2 source and qualification

The [DLR-authored engine analysis](https://link.springer.com/article/10.1007/s12567-025-00625-8) explicitly distinguishes its calculations from unpublished precise engine performance. Its sea-level modeling baseline uses approximately 2,260 kN thrust, a 1.3 m exit diameter and an assumed O/F of 3.6. Keep those as a separate **Estimated** profile, not manufacturer-certified defaults. Do not substitute Raptor 3 or Raptor Vacuum data. Missing vacuum Isp/thrust and mass prevent a complete derived performance card at this stage.

## Formula display example

For the RS-25 source reference above:

    Propellant Mass Flow
    mdot = Fvac / (g0 × Ispvac)
         = 2,278,823.93 N / (9.80665 m/s² × 452 s)
         = 514.105 kg/s

    Fuel Mass Flow
    mdotF = mdot / (1 + O/F)
          = 514.105 / 7
          = 73.444 kg/s

    Oxidizer Mass Flow
    mdotO = mdot − mdotF
          = 440.662 kg/s

Show **Published inputs · Calculated output · Reference operating point** beside this result. Do not present all these decimal places as source measurement precision; the normal UI should round appropriately.


## Implemented prototype and experimental defaults

Flight now sums forces at engine mounts, consumes both propellants, updates vehicle mass, and uses the planet's inverse-square gravity. Newton forces are multiplied by the scene length scale (0.001 world units per metre). Shutdown leaves the rocket in free flight. Launch is blocked for mismatched tank fuel or invalid operating parameters. No aerodynamic drag or thermal/flow-separation model is implemented.

The starting engine presets include explicit estimates to make all five models usable for experiments. They must not be presented as a fully sourced database:

- F-1, RD-180 and RS-25 use the reference thrust, Isp and mass values above. Their **effective exit diameter** is calibrated from the difference between published vacuum and sea-level thrust at 101325 Pa. It is an estimated pressure-response parameter, not a verified physical measurement. RD-180 O/F = 2.72 is an estimate in this preset.
- Merlin 1D experimental inputs: vacuum thrust 914 kN, vacuum Isp 311 s, dry mass 470 kg, O/F 2.36, minimum throttle 40%; effective diameter is calibrated against SpaceX's 845 kN sea-level figure. Values other than that sea-level anchor are assumptions in this implementation.
- Raptor 2 experimental inputs: vacuum thrust 2394.5 kN, vacuum Isp 347 s, dry mass 1630 kg, exit diameter 1.3 m, O/F 3.6, minimum throttle 40%. These are an approximate simulation preset, not independently confirmed manufacturer specifications.
- Fixed density assumptions in kg/m³: RP-1 810, LH2 70.85, methane 422.6, LOX 1141. Tank shell mass is estimated as 15 kg/m² of cylinder surface; frame mass is estimated as 120 kg plus 50 kg per extra mount. Base structure mass defaults to 10000 kg and is editable on Rocket in the Inspector. These assumptions are intentionally separate from measured engine properties.
- Engine center of mass is approximated from its visual height; liquid mass is centered in each tank. Rigid-body inertia is approximated as a cylinder. RD-180 pressure area includes both nozzles, while its force is applied as a single resultant at the engine mount.
- Gimbal compatibility and Sea-level/Vacuum are editable experiment settings. The type label alone does not alter thrust. The reference throttle fractions use the operating point described above; the F-1 preset is treated as fixed-thrust.

Use **Launch**, **Shutdown**, and **Return to assembly**. Choose the correct fuel in **Tanks** and adjust **Initial fill** or capacity if one engine cannot lift the selected mass. **Show formulas** exposes equations; the selected-engine panel shows substituted mass-flow values. Save/load retains engine overrides, tank fuel and initial fill. Current in-flight telemetry is not a saved trajectory.
