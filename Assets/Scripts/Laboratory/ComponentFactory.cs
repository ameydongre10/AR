using System.Collections.Generic;
using UnityEngine;
using ARLab.Electronics;

namespace ARLab.Laboratory
{
    /// <summary>
    /// Builds every lab component procedurally from primitives. Keeps the repository free of
    /// binary model dependencies and guarantees pin colliders always line up with the model.
    /// </summary>
    public static class ComponentFactory
    {
        private const float DipPitch = 0.00254f;
        private const float DipRowSpan = 0.00762f;
        private const float DipLength = 0.0193f;
        private const float DipWidth = 0.0064f;
        private const float DipHeight = 0.0038f;
        private const float PinRadius = 0.00055f;

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// <see cref="Object.Destroy"/> is illegal in edit mode, so the same factory can be
        /// driven from editor tooling, tests and play mode without branching at every call.
        /// </summary>
        private static void Discard(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }

        private static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            Discard(go.GetComponent<Collider>());
            var r = go.GetComponent<MeshRenderer>();
            if (r != null) r.sharedMaterial = mat;
            return go;
        }

        private static GameObject Cyl(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            Discard(go.GetComponent<Collider>());
            var r = go.GetComponent<MeshRenderer>();
            if (r != null) r.sharedMaterial = mat;
            return go;
        }

        private static GameObject Sphere(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            Discard(go.GetComponent<Collider>());
            var r = go.GetComponent<MeshRenderer>();
            if (r != null) r.sharedMaterial = mat;
            return go;
        }

        private static ComponentPin MakePin(Transform parent, string name, Vector3 localPos, int number,
            string signal, PinDirection dir, PinFunction fn, Color tint)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;

            var col = go.AddComponent<SphereCollider>();
            col.radius = 0.9f;
            col.isTrigger = true;

            Cyl(go.transform, "Body", Vector3.zero, new Vector3(PinRadius * 2f, PinRadius * 1.2f, PinRadius * 2f),
                LabMaterials.Colored(tint, 0.75f, 0.6f));

            var pin = go.AddComponent<ComponentPin>();
            pin.Configure(number, signal, dir, fn);
            return pin;
        }

        private static void AddTapCollider(GameObject go, Vector3 size, Vector3 center)
        {
            var col = go.AddComponent<BoxCollider>();
            col.size = size;
            col.center = center;
            col.isTrigger = true;
        }

        // ------------------------------------------------------------------ DIP IC

        /// <summary>14-pin DIP: pins 1-7 on the lower row left to right, 8-14 on the upper row right to left.</summary>
        public static IcComponent CreateIc(IcDefinition def, string name = null)
        {
            var root = new GameObject(name ?? $"IC_{def.PartNumber}");
            var bodyMat = LabMaterials.Colored(new Color(0.09f, 0.09f, 0.10f), 0.1f, 0.35f);
            var notchMat = LabMaterials.Colored(new Color(0.75f, 0.75f, 0.78f));

            Box(root.transform, "Body", Vector3.zero, new Vector3(DipLength, DipHeight, DipWidth), bodyMat);

            // Pin-1 dimple and end notch, matching the real package orientation cue.
            Cyl(root.transform, "Pin1Dot", new Vector3(-DipLength * 0.5f + 0.0016f, DipHeight * 0.5f + 0.00002f, 0f),
                new Vector3(0.0009f, 0.00005f, 0.0009f), notchMat);
            Cyl(root.transform, "Notch", new Vector3(-DipLength * 0.5f - 0.0002f, 0f, 0f),
                new Vector3(0.0006f, DipHeight * 0.7f, 0.0016f), notchMat);

            var pins = new List<ComponentPin>(14);
            for (int n = 1; n <= 14; n++)
            {
                string signal = def.SignalAtPin(n);
                if (signal == null) continue;

                PinDirection dir;
                PinFunction fn;
                Color tint;
                ClassifyPin(def, signal, out dir, out fn, out tint);

                int idx = n - 1;
                bool lower = n <= 7;
                int col = lower ? idx : 13 - idx;
                float x = (col - 3f) * DipPitch;
                float z = (lower ? -1f : 1f) * (DipRowSpan * 0.5f);

                // Lead: a short leg from the body edge down to the board strip.
                Cyl(root.transform, $"Lead{n}", new Vector3(x, -DipHeight * 0.5f + 0.0006f, z),
                    new Vector3(PinRadius * 2f, 0.0016f, PinRadius * 2f), LabMaterials.Colored(tint, 0.8f, 0.6f));

                Vector3 pinPos = new Vector3(x, 0.0004f, z);
                ComponentPin p = MakePin(root.transform, $"Pin{n}", pinPos, n, signal, dir, fn, tint);
                pins.Add(p);
            }

            AddTapCollider(root, new Vector3(DipLength, DipHeight, DipWidth + 0.004f), Vector3.zero);

            var ic = root.AddComponent<IcComponent>();
            ic.Configure(def);
            ic.AssignPins(pins.ToArray());
            ic.SetTint(new Color(0.09f, 0.09f, 0.10f));
            return ic;
        }

        private static void ClassifyPin(IcDefinition def, string signal, out PinDirection dir,
            out PinFunction fn, out Color tint)
        {
            if (signal == "VCC") { dir = PinDirection.Power; fn = PinFunction.Vcc; tint = new Color(0.85f, 0.25f, 0.22f); return; }
            if (signal == "GND") { dir = PinDirection.Power; fn = PinFunction.Gnd; tint = new Color(0.20f, 0.20f, 0.22f); return; }

            bool isOutput = signal.EndsWith("Y");
            dir = isOutput ? PinDirection.Output : PinDirection.Input;
            fn = isOutput ? PinFunction.DataOutput : PinFunction.DataInput;
            tint = isOutput ? new Color(0.95f, 0.80f, 0.30f) : new Color(0.35f, 0.78f, 0.95f);
        }

        // ------------------------------------------------------------------ discrete gate

        /// <summary>Discrete gate module with A / B / Y signal pins plus supply pins.</summary>
        public static LogicGate CreateGate(LogicGateKind kind, string name = null)
        {
            string label = kind == LogicGateKind.Not ? "NOT Gate" : kind == LogicGateKind.And ? "AND Gate" : "OR Gate";
            string part = kind == LogicGateKind.Not ? "74HC04" : kind == LogicGateKind.And ? "74HC08" : "74HC32";

            var root = new GameObject(name ?? label.Replace(" ", ""));
            var shell = LabMaterials.Colored(new Color(0.16f, 0.18f, 0.22f), 0.1f, 0.3f);
            Box(root.transform, "Body", new Vector3(0f, 0.0016f, 0f), new Vector3(0.014f, 0.0032f, 0.010f), shell);

            var pins = new List<ComponentPin>(5);
            float half = 0.00762f * 0.5f;
            if (kind == LogicGateKind.Not)
            {
                pins.Add(MakePin(root.transform, "PinA", new Vector3(-0.004f, 0f, -half), 1, "A", PinDirection.Input, PinFunction.DataInput, new Color(0.35f, 0.78f, 0.95f)));
                pins.Add(MakePin(root.transform, "PinY", new Vector3(0.004f, 0f, -half), 2, "Y", PinDirection.Output, PinFunction.DataOutput, new Color(0.95f, 0.80f, 0.30f)));
            }
            else
            {
                pins.Add(MakePin(root.transform, "PinA", new Vector3(-0.004f, 0f, -half), 1, "A", PinDirection.Input, PinFunction.DataInput, new Color(0.35f, 0.78f, 0.95f)));
                pins.Add(MakePin(root.transform, "PinB", new Vector3(-0.002f, 0f, half), 2, "B", PinDirection.Input, PinFunction.DataInput, new Color(0.35f, 0.78f, 0.95f)));
                pins.Add(MakePin(root.transform, "PinY", new Vector3(0.004f, 0f, -half), 3, "Y", PinDirection.Output, PinFunction.DataOutput, new Color(0.95f, 0.80f, 0.30f)));
            }
            pins.Add(MakePin(root.transform, "PinVcc", new Vector3(0.004f, 0f, half), 8, "VCC", PinDirection.Power, PinFunction.Vcc, new Color(0.85f, 0.25f, 0.22f)));
            pins.Add(MakePin(root.transform, "PinGnd", new Vector3(-0.004f, 0f, half), 7, "GND", PinDirection.Power, PinFunction.Gnd, new Color(0.20f, 0.20f, 0.22f)));

            AddTapCollider(root, new Vector3(0.016f, 0.006f, 0.018f), new Vector3(0f, 0.002f, 0f));

            var gate = root.AddComponent<LogicGate>();
            gate.Configure(kind, label, part);
            gate.AssignPins(pins.ToArray());
            gate.SetTint(new Color(0.16f, 0.18f, 0.22f));
            return gate;
        }

        // ------------------------------------------------------------------ input switch

        public static InputSwitch CreateSwitch(string signal = "A", string name = null)
        {
            var root = new GameObject(name ?? $"Switch_{signal}");
            var body = LabMaterials.Colored(new Color(0.85f, 0.86f, 0.88f), 0.05f, 0.3f);
            Box(root.transform, "Body", new Vector3(0f, 0.003f, 0f), new Vector3(0.010f, 0.006f, 0.008f), body);
            Box(root.transform, "Lever", new Vector3(0.0015f, 0.0068f, 0f), new Vector3(0.0016f, 0.0022f, 0.0016f),
                LabMaterials.Colored(new Color(0.30f, 0.32f, 0.36f)));

            var pins = new List<ComponentPin>(3)
            {
                MakePin(root.transform, "PinOut", new Vector3(0f, 0.0006f, -0.006f), 1, "OUT", PinDirection.Output, PinFunction.DataOutput, new Color(0.95f, 0.80f, 0.30f)),
                MakePin(root.transform, "PinVcc", new Vector3(-0.003f, 0.0006f, 0.006f), 8, "VCC", PinDirection.Power, PinFunction.Vcc, new Color(0.85f, 0.25f, 0.22f)),
                MakePin(root.transform, "PinGnd", new Vector3(0.003f, 0.0006f, 0.006f), 7, "GND", PinDirection.Power, PinFunction.Gnd, new Color(0.20f, 0.20f, 0.22f))
            };

            AddTapCollider(root, new Vector3(0.012f, 0.010f, 0.014f), new Vector3(0f, 0.004f, 0f));

            var sw = root.AddComponent<InputSwitch>();
            sw.Configure(signal);
            sw.AssignPins(pins.ToArray());
            sw.SetTint(new Color(0.85f, 0.86f, 0.88f));
            return sw;
        }

        // ------------------------------------------------------------------ LED

        public static LedIndicator CreateLed(string signal = "F", string name = null)
        {
            var root = new GameObject(name ?? $"LED_{signal}");
            var lensMat = LabMaterials.Colored(new Color(0.16f, 0.18f, 0.20f), 0f, 0.9f);
            Cyl(root.transform, "Base", new Vector3(0f, 0.0006f, 0f), new Vector3(0.0022f, 0.0006f, 0.0022f),
                LabMaterials.Colored(new Color(0.20f, 0.20f, 0.22f)));
            Sphere(root.transform, "Lens", new Vector3(0f, 0.0022f, 0f), new Vector3(0.0026f, 0.0028f, 0.0026f), lensMat);

            var pins = new List<ComponentPin>(1)
            {
                MakePin(root.transform, "PinAnode", new Vector3(0f, 0.0006f, -0.0035f), 1, "ANODE", PinDirection.Input, PinFunction.DataInput, new Color(0.95f, 0.80f, 0.30f))
            };

            AddTapCollider(root, new Vector3(0.006f, 0.006f, 0.009f), new Vector3(0f, 0.002f, -0.001f));

            var led = root.AddComponent<LedIndicator>();
            led.Configure(signal);
            led.AssignPins(pins.ToArray());
            led.SetTint(new Color(0.25f, 0.85f, 0.45f));
            return led;
        }

        // ------------------------------------------------------------------ probe

        public static LogicProbe CreateProbe(string label = "P1", string name = null)
        {
            var root = new GameObject(name ?? $"Probe_{label}");
            Cyl(root.transform, "Body", new Vector3(0f, 0.004f, 0f), new Vector3(0.0012f, 0.004f, 0.0012f),
                LabMaterials.Colored(new Color(0.25f, 0.27f, 0.32f)));

            var pins = new List<ComponentPin>(1)
            {
                MakePin(root.transform, "Tip", new Vector3(0f, 0.0004f, 0f), 1, "PROBE", PinDirection.Input, PinFunction.DataInput, new Color(0.35f, 0.78f, 0.95f))
            };

            AddTapCollider(root, new Vector3(0.004f, 0.010f, 0.004f), new Vector3(0f, 0.005f, 0f));

            var probe = root.AddComponent<LogicProbe>();
            probe.Configure(label);
            probe.AssignPins(pins.ToArray());
            probe.SetTint(new Color(0.25f, 0.27f, 0.32f));
            return probe;
        }

        // ------------------------------------------------------------------ power supply

        public static PowerSupply CreatePowerSupply(string name = "PowerSupply")
        {
            var root = new GameObject(name);
            var body = LabMaterials.Colored(new Color(0.20f, 0.22f, 0.26f), 0.2f, 0.35f);
            Box(root.transform, "Body", new Vector3(0f, 0.012f, 0f), new Vector3(0.075f, 0.024f, 0.050f), body);

            Sphere(root.transform, "Indicator", new Vector3(-0.028f, 0.016f, 0.020f),
                new Vector3(0.0035f, 0.0035f, 0.0035f),
                LabMaterials.Colored(new Color(0.20f, 0.21f, 0.23f), 0f, 0.9f));

            var readoutGo = new GameObject("Readout");
            readoutGo.transform.SetParent(root.transform, false);
            readoutGo.transform.localPosition = new Vector3(0.008f, 0.016f, 0.024f);
            readoutGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var tm = readoutGo.AddComponent<TextMesh>();
            tm.text = "0.0 V  OFF";
            tm.fontSize = 64;
            tm.characterSize = 0.0009f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = new Color(0.80f, 0.35f, 0.35f);

            var pins = new List<ComponentPin>(2)
            {
                MakePin(root.transform, "VccPost", new Vector3(0.030f, 0.0025f, -0.018f), 1, "VCC_OUT", PinDirection.Output, PinFunction.DataOutput, new Color(0.85f, 0.25f, 0.22f)),
                MakePin(root.transform, "GndPost", new Vector3(0.030f, 0.0025f, 0.018f), 2, "GND_OUT", PinDirection.Output, PinFunction.DataOutput, new Color(0.20f, 0.20f, 0.22f))
            };

            AddTapCollider(root, new Vector3(0.078f, 0.030f, 0.054f), new Vector3(0f, 0.012f, 0f));

            var psu = root.AddComponent<PowerSupply>();
            psu.AssignPins(pins.ToArray());
            psu.SetTint(new Color(0.20f, 0.22f, 0.26f));
            psu.ApplyVisual();
            return psu;
        }
    }
}
