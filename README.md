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

- Unity 6
- AR Foundation
- ARCore (Android)
- Visual Studio
- C#
- Blender (for custom models)

---

# Project Architecture

```
Project
│
├── Assets
│   ├── Models
│   ├── Materials
│   ├── Prefabs
│   ├── Scenes
│   ├── Scripts
│   ├── UI
│   ├── Animations
│   ├── Textures
│   └── Audio
│
├── Packages
│
├── ProjectSettings
│
└── README.md
```

---

# Unity Scene Structure

```
Main Scene

AR Session

AR Session Origin

AR Camera

Plane Manager

Raycast Manager

Lighting

Directional Light

Canvas

AR Laboratory

Breadboard

Power Supply

IC7404

IC7408

IC7432

Hookup Wires

Patch Cords

Tutorial Panel

Simulation Manager
```

---

# Scripts

```
Scripts

ARPlacementManager.cs

ObjectManipulator.cs

BreadboardManager.cs

PowerSupply.cs

WireManager.cs

WireRenderer.cs

SnapManager.cs

DigitalLogicSimulator.cs

IC7404.cs

IC7408.cs

IC7432.cs

SimulationManager.cs

TutorialManager.cs

UIManager.cs
```

---

# Features

## Augmented Reality

✔ Plane Detection

✔ Plane Tracking

✔ Tap to Place Lab

✔ Pinch to Scale

✔ Rotate Laboratory

✔ Drag Laboratory

---

## Interaction

✔ Pick Objects

✔ Move Components

✔ Rotate Components

✔ Snap to Breadboard

✔ Remove Components

✔ Auto Alignment

---

## Breadboard Simulation

- Interactive breadboard holes
- Intelligent snapping
- Automatic wiring
- Correct rail connections
- Collision detection

---

## Wire System

- Dynamic wire generation
- Automatic routing
- Curved wire rendering
- Multiple colors
- Delete wire functionality

---

## Logic Simulation

Supports

- NOT Gate
- AND Gate
- OR Gate

Real-time logic propagation.

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

Green LED

Logic HIGH

Red LED

Logic LOW

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

The developed AR laboratory successfully simulated the implementation of Boolean functions using TTL logic gates.

Students were able to:

- Place virtual components accurately.
- Connect the circuit correctly.
- Observe logic propagation.
- Verify SOP implementation.
- Verify POS implementation.
- Compare theoretical and practical outputs.
- Perform experiments without physical hardware.

The results matched the expected truth table for all tested input combinations.

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

The AR Digital Electronics Laboratory successfully demonstrates the implementation of Boolean functions using SOP and POS forms within an immersive learning environment. By combining Unity, AR Foundation, and digital logic simulation, the project provides an engaging alternative to conventional electronics laboratories. Students can safely practice circuit construction, validate logical expressions, and gain hands-on experience through interactive visualization, making digital electronics education more accessible and effective.

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
