using System.IO;
using System.Linq;
using System.Xml.Linq;
using NUnit.Framework;

namespace ARLab.Tests
{
    /// <summary>
    /// An AR app cannot start without the camera permission, and none of the ARCore AARs
    /// declare it, so the project has to supply it. This is checked here because nothing in
    /// the C# assembly would fail to compile if the manifest were deleted, and the mistake
    /// only shows up as a black camera on a physical device.
    /// </summary>
    public class AndroidManifestTests
    {
        private const string LibraryManifest = "Assets/Plugins/Android/ARPermissions.androidlib/AndroidManifest.xml";

        private static readonly XNamespace AndroidNs = "http://schemas.android.com/apk/res/android";

        private static XElement LoadManifest()
        {
            Assert.IsTrue(File.Exists(LibraryManifest),
                $"{LibraryManifest} is missing; the built APK would have no camera permission and AR would not start.");
            return XDocument.Load(LibraryManifest).Root;
        }

        /// <summary>Reads an "android:name"-style attribute; the colon is a namespace, not part of the name.</summary>
        private static string Attr(XElement e, string localName) => e?.Attribute(AndroidNs + localName)?.Value;

        [Test]
        public void ManifestIsWellFormedXml()
        {
            var root = LoadManifest();
            Assert.IsNotNull(root);
            Assert.AreEqual("manifest", root.Name.LocalName);
        }

        [Test]
        public void ManifestDeclaresCameraPermission()
        {
            var perms = LoadManifest().Elements("uses-permission")
                             .Select(e => Attr(e, "name"))
                             .Where(n => n != null)
                             .ToList();

            Assert.Contains("android.permission.CAMERA", perms,
                "ARCore needs the CAMERA permission and no AAR in this project declares it.");
        }

        [Test]
        public void ManifestRequiresArHardwareFeature()
        {
            var feature = LoadManifest().Elements("uses-feature")
                              .FirstOrDefault(e => Attr(e, "name") == "android.hardware.camera.ar");

            Assert.IsNotNull(feature, "The build must declare that it needs AR-capable hardware.");
            Assert.AreEqual("true", Attr(feature, "required"),
                "AR support must be required; 'false' would offer the app to devices that cannot run it.");
        }

        [Test]
        public void ManifestMarksArCoreAsRequired()
        {
            var meta = LoadManifest().Descendants("meta-data")
                          .FirstOrDefault(e => Attr(e, "name") == "com.google.ar.core");

            Assert.IsNotNull(meta, "The com.google.ar.core meta-data entry is required for ARCore to run.");
            Assert.AreEqual("required", Attr(meta, "value"),
                "'required' stops ARCore silently falling back to a non-AR session on unsupported devices.");
        }
    }
}
