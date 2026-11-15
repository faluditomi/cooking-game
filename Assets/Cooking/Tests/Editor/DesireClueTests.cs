using System.Collections.Generic;
using Cooking.Prototype;
using NUnit.Framework;
using UnityEngine;

namespace Cooking.Tests
{
    public class DesireClueTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach(Object obj in created) Object.DestroyImmediate(obj);
            created.Clear();
        }

        private FlavourZone Zone(FlavourProfile min, FlavourProfile max)
        {
            FlavourZone zone = ScriptableObject.CreateInstance<FlavourZone>();
            zone.Configure("Desire", min, max);
            created.Add(zone);
            return zone;
        }

        [Test]
        public void Warmth_ComparesWithTheBestSoFar()
        {
            Assert.AreEqual(Warmth.First, DesireClue.Compare(3f, float.PositiveInfinity));
            Assert.AreEqual(Warmth.Warmer, DesireClue.Compare(2f, 3f));
            Assert.AreEqual(Warmth.Colder, DesireClue.Compare(4f, 3f));
            Assert.AreEqual(Warmth.Same, DesireClue.Compare(3.1f, 3f));
        }

        [Test]
        public void MostOff_FindsTheAxisFurthestOutside()
        {
            FlavourProfile max = FlavourProfile.Filled(10f);
            max.salty = 3f;
            FlavourZone desire = Zone(new FlavourProfile { sweet = 4f }, max);

            // Sweet is 3 short; salt is 1 over. Sweet is the bigger miss.
            Assert.IsTrue(DesireClue.MostOff(new FlavourProfile { sweet = 1f, salty = 4f }, desire, out FlavourAxis axis, out bool more));
            Assert.AreEqual(FlavourAxis.Sweet, axis);
            Assert.IsTrue(more);

            Assert.IsTrue(DesireClue.MostOff(new FlavourProfile { sweet = 5f, salty = 6f }, desire, out axis, out more));
            Assert.AreEqual(FlavourAxis.Salty, axis);
            Assert.IsFalse(more);

            Assert.IsFalse(DesireClue.MostOff(new FlavourProfile { sweet = 5f, salty = 2f }, desire, out _, out _), "inside the zone");
        }

        [Test]
        public void Describe_CombinesWarmthAndHint()
        {
            FlavourZone desire = Zone(new FlavourProfile { pain = 8f }, FlavourProfile.Filled(10f));

            string clue = DesireClue.Describe(new FlavourProfile { pain = 3f }, desire, Warmth.Warmer);

            StringAssert.StartsWith("That's closer", clue);
            StringAssert.Contains("Hotter", clue);
        }

        [Test]
        public void Relationship_RemembersTheClosestDish()
        {
            CustomerRelationship relationship = new CustomerRelationship(new Customer());

            Assert.AreEqual(Warmth.First, relationship.RecordDesireDistance(4f));
            Assert.AreEqual(Warmth.Warmer, relationship.RecordDesireDistance(2f));
            Assert.AreEqual(Warmth.Colder, relationship.RecordDesireDistance(3f), "compared with the best (2), not the last");
            Assert.AreEqual(2f, relationship.BestDesireDistance);
        }
    }
}
