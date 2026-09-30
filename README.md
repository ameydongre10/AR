# Interactive AR Digital Electronics Laboratory
### Experiment: Implementation of Boolean Functions Using Logic Gates in SOP and POS Forms

<p align="center">
<img src="docs/images/lab_preview.png" width="800">
</p>
<p>https://ar-digital-electronics-laboratory-e.vercel.app/</p>
---

## 📌 Project Overview

This project presents an **Interactive Augmented Reality (AR) Digital Electronics Laboratory** developed using **Unity 6** and **AR Foundation**. The laboratory enables students to perform digital electronics experiments in an immersive environment by interacting with virtual electronic components placed in the real world.

The experiment demonstrates the **implementation of Boolean functions using logic gates in both Sum of Products (SOP) and Product of Sums (POS) forms** using standard TTL integrated circuits.

Unlike traditional laboratories, the AR environment provides real-time guidance, intelligent component placement, digital logic simulation, and interactive visualization of circuit behavior.

---

# Objectives

The objectives of this experiment are:

- Understand Boolean algebra implementation.
- Learn SOP and POS realization.
- Understand digital logic circuit construction.
- Perform practical implementation using ICs.
- Observe output for different input combinations.
- Verify the truth table experimentally.
- Develop familiarity with breadboard wiring.
- Experience an interactive AR-based laboratory.

---

# Learning Outcomes

After completing this experiment, students will be able to:

- Convert Boolean expressions into SOP and POS forms.
- Identify TTL logic ICs.
- Construct digital logic circuits.
- Connect power supply correctly.
- Place ICs on a breadboard.
- Perform digital circuit testing.
- Analyze circuit outputs.
- Use AR technology for virtual laboratory experiments.

---

# Experiment Details

## Experiment Title

**Implementation of the Given Boolean Function Using Logic Gates in SOP and POS Forms**

---

# Theory

Boolean algebra forms the mathematical foundation of digital electronics.

A Boolean function can be implemented in two standard forms:

## Sum of Products (SOP)

The SOP form consists of multiple AND terms connected using OR gates.

Example

```
F = A'B + AB'
```

Implementation:

- NOT Gate
- AND Gate
- OR Gate

---

## Product of Sums (POS)

The POS form consists of multiple OR terms connected using AND gates.

Example

```
F = (A+B)(A'+B')
```

Implementation:

- OR Gate
- NOT Gate
- AND Gate

---

# Hardware Components

| Component | Quantity |
|-----------|----------|
| Breadboard Trainer Kit | 1 |
| DC Power Supply | 1 |
| Hookup Wires | Multiple |
| Patch Cords | Multiple |
| IC 7404 (Hex NOT Gate) | 1 |
| IC 7408 (Quad AND Gate) | 1 |
| IC 7432 (Quad OR Gate) | 1 |

---

# Software Requirements

- Unity `6000.4.3f1`
- AR Foundation `1.0.2` (ARCore `6.2.0` on Android)
- Input System `1.14.2`, URP `17.4.0`
- Android device with ARCore support, API 25 or newer, for device testing
- No external art tools: all geometry and UI are generated at runtime from code

---

# Project Architecture

```
AR
├── Assets
│   ├── Editor
│   │   ├── ARLab.Editor.asmdef
│   │   └── LabSceneBuilder.cs        ← scene + Android + ARCore loader tooling
│   ├── Scenes
│   │   └── Main.unity                ← one GameManager, everything else at runtime
│   ├── Scripts
│   │   ├── ARLab.Runtime.asmdef
│   │   ├── AR/                       ← session, raycast, planes, placement, touch
│   │   ├── Core/                     ← GameManager, state machine, event bus, events
│   │   ├── Electronics/              ← logic value, netlist, pins, TTL parts
│   │   ├── Laboratory/               ← breadboard, factory, materials, lab manager
│   │   ├── Simulation/               ← solver, expression parser, truth table, validator
│   │   └── UI/                       ← HUD, tutorial
│   ├── Tests
│   │   └── EditMode/                 ← NUnit suites (no PlayMode tests yet)
│   └── XR/                           ← XR Plug-in Management settings (ARCore loader)
├── Packages
├── ProjectSettings
└── README.md
```

Pinned in `Packages/manifest.json`: Unity `6000.4.3f1`, AR Foundation `1.0.2`, Input System
`1.14.2`, URP `17.4.0`, ARCore `6.2.0`, XR Plug-in Management `4.5.3`.

---

# Setup

```bash
git clone https://github.com/ameydongre10/AR.git
```

1. Open with **Unity 6000.4.3f1** and let it resolve packages.
2. Run **AR Lab → Configure ARCore Loader** (creates the per-build-target XR settings and
   assigns the ARCore loader; without it the app builds but no session ever starts).
3. Run **AR Lab → Configure Android Player Settings** (ARM64, IL2CPP, min API 25, Vulkan with
   an OpenGLES3 fallback).
4. Run **AR Lab → Rebuild Main Scene** and **AR Lab → Set As Startup Scene**.
5. Press Play. With no XR loader active in the editor the app runs in simulated mode and the
   **Place** button puts the bench in front of the camera, so the whole experiment is reachable
   without a headset.

Tests, headless:

```bash
Unity -batchmode -nographics -projectPath . \
  -runTests -testPlatform EditMode \
  -testResults results.xml -logFile run.log
```

Current state: **95 EditMode tests, all passing.**

---

# Unity Scene Structure

`Assets/Scenes/Main.unity` contains a single GameObject holding one `GameManager`. Everything
else — the XR rig, the bench, the HUD, the tutorial — is assembled at runtime by that same
component, so there is no hand-wired scene to drift out of sync with the code.

```text
Main.unity
└── ARLab                      ← GameManager, the only authored object
    ├── ARSession              ← ARSession, ARPlaneManager, ARRaycastManager,
    │                             ARSessionController, ArPlaneVisualizer, ARRaycastController
    ├── Origin                 ← XROrigin
    │   └── AR Camera          ← Camera, AudioListener, ARCameraManager
    ├── LabRoot                ← inactive until the rig is placed
    │   ├── ComponentAnchor    ← spawn parent for library parts
    │   ├── Breadboard         ← Breadboard (deck + picking collider, built in code)
    │   └── PowerSupply        ← procedural PSU with TextMesh readout
    ├── Laboratory             ← LaboratoryManager
    ├── ARPlacement            ← ARPlacementManager
    ├── ARInteraction          ← ArInteractionManager
    ├── Tutorial               ← TutorialController
    ├── HudCanvas              ← LabHud (canvas, panels, buttons; also built in code)
    └── EventSystem            ← EventSystem + InputSystemUIInputModule
```

Regenerate the scene with **AR Lab → Rebuild Main Scene**. The same code path runs in play mode
and in the EditMode test suite (`Assets/Tests/EditMode/SceneAssemblyTests.cs`), so a wiring
mistake fails a test rather than showing up only on a device.

---

# Scripts

`Assets/Scripts/ARLab.Runtime.asmdef` holds the runtime code in six namespaces:

| Namespace | Files | Responsibility |
|---|---|---|
| `ARLab.Core` | 4 | `GameManager`, `ArStateMachine`, `EventBus`, `GameEvents` |
| `ARLab.Electronics` | 11 | `LogicValue`, `ConnectionGraph`, pins, TTL parts, `IcComponent`, `LogicGate` |
| `ARLab.Laboratory` | 5 | `Breadboard`, `BreadboardGrid`, `ComponentFactory`, `LabMaterials`, `LaboratoryManager` |
| `ARLab.Simulation` | 4 | `DigitalLogicSimulator`, `BooleanExpression`, `TruthTableEvaluator`, `ExperimentValidator` |
| `ARLab.AR` | 5 | session boot, raycasts, plane visualizer, placement, touch routing |
| `ARLab.UI` | 2 | `LabHud`, `TutorialController` |

Notable design points:

- **Three-valued logic.** `LogicValue` is `Low`/`High`/`Undefined`; there is no `bool` anywhere
  in the solver, so a floating input cannot silently read as `false`.
- **Solver, not formula.** `DigitalLogicSimulator` runs the real netlist to a fixpoint and
  reports contention, floating nets and oscillation. `ExperimentValidator` checks structure, and
  the truth table comes from `TruthTableEvaluator` simulating each input combination — nothing
  is compared against a hard-coded expected result.
- **Authoritative graph.** `ConnectionGraph` uses union-find over terminals; breadboard sockets
  and rails are pre-tied at construction, so wiring a rail is a topology fact rather than a rule.
- **Geometry from code.** `ComponentFactory` and `LabHud.BuildRuntimeUi` build meshes, materials
  and the entire panel set procedurally, so the repository carries no binary art.
- **One EventBus.** Systems publish domain events; the HUD and tutorial subscribe. No system
  holds a reference to another except the managers wired in `GameManager.BuildIfNeeded`.

---

# Features

## Augmented Reality

✔ Horizontal plane detection, with a procedurally generated overlay

✔ Real AR raycast placement, plus an editor/simulated fallback

✔ Tap to place the rig

✔ Drag, pinch to scale, twist to rotate, with scale and tilt clamped

✔ `ArStateMachine` gate: illegal actions are rejected, not silently ignored

---

## Interaction

✔ Pick parts by collider

✔ Move, rotate and scale the placed rig

✔ Snap parts into breadboard sockets and DIP positions

✔ Select, delete and reset

✔ Two-finger gesture identity is tracked, so a finger lift cannot strand a gesture

---

## Breadboard Simulation

- 63-column board, rows A–J, plus power rails
- Socket picking and snapping to a real hole
- Split-rail option, with the two halves correctly isolated
- Rail connectivity comes from the netlist topology, not from proximity rules

---

## Wire System

- Two-tap wiring: tap a pin, then tap a target
- Preview wire follows the cursor before committing
- Two outputs on one net are rejected as a contention
- Cancel and delete are always available

---

## Logic Simulation

Three-valued, fixpoint-based, netlist-driven:

- `7404` (hex inverter), `7408` (quad AND), `7432` (quad OR), each gate addressed independently
- Reports floating nets, output contention and combinational oscillation
- Evaluates against the real netlist, so a miswired board fails the check

---

## Power Supply

Features

- ON/OFF Switch
- +5V Output
- Ground
- LED Indicator
- Voltage Simulation

---

## Output Indicators

- `LedIndicator`: green for HIGH, red for LOW, dark for `Undefined`
- `LogicProbe`: reads any net and shows HIGH/LOW/– on a TextMesh
- `PowerSupply`: +5V/GND posts with a live TextMesh voltage readout

---

## Guidance and Verification

- `TutorialController` advances on predicates over the live bench, not a timer
- `ExperimentValidator` reports per-check pass/warn/fail with reasons
- `TruthTableEvaluator` simulates every input combination and renders the table
- `LabHud` builds its whole panel set in code, so there is no unwired canvas

---

# Procedure

## Step 1

Launch the application.

---

## Step 2

Detect a flat surface.

---

## Step 3

Place the virtual electronics laboratory.

---

## Step 4

Switch ON the power supply.

---

## Step 5

Insert IC 7404.

---

## Step 6

Insert IC 7408.

---

## Step 7

Insert IC 7432.

---

## Step 8

Connect power pins.

---

## Step 9

Connect inputs using jumper wires.

---

## Step 10

Complete the SOP/POS circuit.

---

## Step 11

Provide input combinations.

---

## Step 12

Observe LED output.

---

## Step 13

Verify truth table.

---

# Truth Table

| A | B | Output |
|---|---|--------|
|0|0|0|
|0|1|1|
|1|0|1|
|1|1|0|

*(Replace with the truth table of the assigned Boolean function.)*

---

# Expected Output

- Circuit assembled successfully.
- Proper IC placement.
- Correct wiring.
- Digital simulation executes correctly.
- LED indicates HIGH/LOW.
- Truth table matches theoretical values.

---

# Experimental Results

Simulation and validation are automated. `DigitalLogicSimulatorTests` builds circuits on a real
breadboard topology and asserts against the netlist, covering: inverter and two-input gate truth
tables, series inversion for SOP/POS, LED and probe output, split-rail isolation, floating nets,
output contention, and a complete XOR experiment validated end to end.

`SceneAssemblyTests` assembles the runtime graph from an empty scene and asserts that every
manager, panel, collider and button exists and is wired, which is what previously only surfaced
on a physical device.

No hardware or device run has been performed, so no on-device results are claimed here.

---

# Advantages

- Interactive learning
- Safe experimentation
- No hardware damage
- Low maintenance
- Portable laboratory
- Repeatable experiments
- Cost effective
- Real-time simulation
- Immersive visualization

---

# Future Scope

- Voice-controlled interaction
- AI-based wiring assistance
- Hand tracking
- Multi-user collaborative AR
- Remote laboratories
- Additional TTL IC library
- FPGA simulation
- Automatic report generation
- Cloud-based experiment storage

---

# Screenshots

Referenced in this README but not yet present in the repository:

```
docs/images/

lab_preview.png

placement.png

wire_connection.png

simulation.png

output_high.png

output_low.png
```

---

# Conclusion

The AR Digital Electronics Laboratory implements Boolean functions in SOP and POS form using
standard TTL ICs, with the circuit behaviour derived from a real netlist rather than a lookup
table. Scene assembly, geometry and UI are all generated from code, so the project is
reproducible from a fresh clone with no binary assets to source or import. AR placement,
plane detection and the full experiment flow are complete in code, but ARCore tracking and
in-app behaviour have not yet been validated on physical hardware.

---

# References

1. M. Morris Mano, *Digital Design*, Pearson.
2. R. P. Jain, *Modern Digital Electronics*.
3. Floyd, *Digital Fundamentals*.
4. Unity Documentation.
5. AR Foundation Documentation.
6. ARCore Documentation.
7. Texas Instruments TTL Logic Datasheets.
8. IEEE Digital Electronics Standards.

---

# Authors

**Developed By**

Name: _______________________

Department: Computer Science & Engineering

Institution: _______________________

Academic Year: _______________________

---

# License

This project is intended for educational and research purposes.
