// Guards the module layout from ADR 0002: every module has a Core assembly,
// and no Core assembly depends on UnityEngine, so its logic stays testable in Edit mode.
using System;
using System.Linq;
using NUnit.Framework;

namespace VrFencing.Tests
{
    public class ModuleLayoutTests
    {
        private static readonly string[] Modules =
        {
            "Strip", "Blade", "Dummy", "Drills", "Session", "UI", "Tracking", "Settings",
        };

        [Test]
        public void EveryModuleHasACoreAndAUnityAssembly()
        {
            var loadedNames = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetName().Name).ToArray();

            foreach (var module in Modules)
            {
                Assert.That(loadedNames, Does.Contain($"VrFencing.{module}.Core"));
                Assert.That(loadedNames, Does.Contain($"VrFencing.{module}.Unity"));
            }
        }

        [Test]
        public void NoCoreAssemblyReferencesUnityEngine()
        {
            var coreAssemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => assembly.GetName().Name.StartsWith("VrFencing.") && assembly.GetName().Name.EndsWith(".Core"))
                .ToArray();

            Assert.That(coreAssemblies, Has.Length.EqualTo(Modules.Length));
            foreach (var assembly in coreAssemblies)
            {
                var engineReferences = assembly.GetReferencedAssemblies()
                    .Where(reference => reference.Name.StartsWith("UnityEngine") || reference.Name.StartsWith("UnityEditor"))
                    .Select(reference => reference.Name);
                Assert.That(engineReferences, Is.Empty, $"{assembly.GetName().Name} references Unity.");
            }
        }
    }
}
