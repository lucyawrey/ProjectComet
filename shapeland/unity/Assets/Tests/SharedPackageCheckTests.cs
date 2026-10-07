using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ShapeLand.Client.Tests
{
    // Runs step 2b's check scene in the editor's Play mode, the same check the desktop and web builds run.
    public class SharedPackageCheckTests
    {
        [UnityTest]
        public IEnumerator CheckScenePasses()
        {
            SceneManager.LoadScene("SharedPackageCheck");
            yield return null;
            var check = Object.FindAnyObjectByType<SharedPackageCheck>();
            Assert.That(check, Is.Not.Null, "The check scene has no SharedPackageCheck.");

            var deadline = Time.realtimeSinceStartup + 30;
            while (check.Passed == null && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(check.Passed, Is.True, check.Result == "" ? "The check didn't finish in 30 s." : check.Result);
        }
    }
}
