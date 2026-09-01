using System.Globalization;
using CustomNavigation.Tests.Shared;
using NUnit.Framework;

namespace CustomNavigation.Editor.Tests
{
    public sealed class NavigationWireCodecTests
    {
        [Test]
        public void SharedConformanceCorpusPassesUnderNonInvariantLocale()
        {
            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                Assert.That(
                    NavigationWireConformanceFixtures.Run(),
                    Is.EqualTo("P04_WIRE_CONFORMANCE_OK valid=4 invalid=15"));
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }
    }
}
