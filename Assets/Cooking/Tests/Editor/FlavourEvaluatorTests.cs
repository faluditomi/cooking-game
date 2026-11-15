using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Cooking.Tests
{
    public class FlavourEvaluatorTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach(Object obj in created) Object.DestroyImmediate(obj);
            created.Clear();
        }

        private FlavourZone Zone(string name, FlavourProfile min, FlavourProfile max)
        {
            FlavourZone zone = ScriptableObject.CreateInstance<FlavourZone>();
            zone.Configure(name, min, max);
            created.Add(zone);
            return zone;
        }

        private static FlavourProfile Open => FlavourProfile.Filled(FlavourProfile.MaxValue);

        [Test]
        public void NewZone_CoversWholeRadar()
        {
            FlavourZone zone = ScriptableObject.CreateInstance<FlavourZone>();
            created.Add(zone);

            Assert.IsTrue(zone.Contains(FlavourProfile.Zero));
            Assert.IsTrue(zone.Contains(Open));
        }

        [Test]
        public void Score_IsOneInside_AndFallsOffOutside()
        {
            FlavourProfile max = Open;
            max.salty = 4f;
            FlavourZone zone = Zone("Mild", FlavourProfile.Zero, max);

            Assert.AreEqual(1f, FlavourEvaluator.Score(new FlavourProfile { salty = 3f }, zone, 2f));
            Assert.AreEqual(0.5f, FlavourEvaluator.Score(new FlavourProfile { salty = 5f }, zone, 2f), 0.0001f);
            Assert.AreEqual(0f, FlavourEvaluator.Score(new FlavourProfile { salty = 7f }, zone, 2f));
        }

        [Test]
        public void Score_WithZeroFalloff_IsBinary()
        {
            FlavourProfile max = Open;
            max.salty = 4f;
            FlavourZone zone = Zone("Mild", FlavourProfile.Zero, max);

            Assert.AreEqual(1f, FlavourEvaluator.Score(new FlavourProfile { salty = 4f }, zone, 0f));
            Assert.AreEqual(0f, FlavourEvaluator.Score(new FlavourProfile { salty = 4.01f }, zone, 0f));
        }

        [Test]
        public void DistanceOutside_CombinesAxes()
        {
            FlavourProfile max = Open;
            max.salty = 4f;
            max.sweet = 4f;
            FlavourZone zone = Zone("Mild", FlavourProfile.Zero, max);

            float distance = zone.DistanceOutside(new FlavourProfile { salty = 7f, sweet = 8f });

            Assert.AreEqual(5f, distance, 0.0001f);
        }

        [Test]
        public void FindBestZone_PicksHighestScore()
        {
            FlavourZone sweetZone = Zone("Sweet", new FlavourProfile { sweet = 6f }, Open);
            FlavourZone saltyZone = Zone("Salty", new FlavourProfile { salty = 6f }, Open);
            FlavourProfile dish = new FlavourProfile { salty = 7f, sweet = 5f };

            FlavourZoneMatch match = FlavourEvaluator.FindBestZone(dish, new[] { sweetZone, saltyZone }, 2f);

            Assert.AreSame(saltyZone, match.Zone);
            Assert.IsTrue(match.IsInside);
        }

        [Test]
        public void FindBestZone_ReturnsNoMatch_WhenEverythingIsFar()
        {
            FlavourZone sweetZone = Zone("Sweet", new FlavourProfile { sweet = 8f }, Open);

            FlavourZoneMatch match = FlavourEvaluator.FindBestZone(FlavourProfile.Zero, new[] { sweetZone }, 2f);

            Assert.IsFalse(match.IsMatch);
            Assert.IsNull(match.Zone);
        }

        [Test]
        public void FindSharedZone_RequiresBothProfilesToFit()
        {
            FlavourZone saltyZone = Zone("Salty", new FlavourProfile { salty = 6f }, Open);
            FlavourZone sweetZone = Zone("Sweet", new FlavourProfile { sweet = 6f }, Open);
            FlavourZone[] zones = { saltyZone, sweetZone };

            FlavourProfile ordered = new FlavourProfile { salty = 8f };
            FlavourProfile goodSubstitute = new FlavourProfile { salty = 7f, savoury = 3f };
            FlavourProfile badSubstitute = new FlavourProfile { sweet = 8f };

            FlavourZoneMatch good = FlavourEvaluator.FindSharedZone(ordered, goodSubstitute, zones, 0f);
            FlavourZoneMatch bad = FlavourEvaluator.FindSharedZone(ordered, badSubstitute, zones, 0f);

            Assert.AreSame(saltyZone, good.Zone);
            Assert.IsFalse(bad.IsMatch);
        }
    }
}
