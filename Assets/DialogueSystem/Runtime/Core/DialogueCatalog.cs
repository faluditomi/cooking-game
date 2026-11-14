using System;
using System.Collections.Generic;
using UnityEngine;

namespace DialogueSystem
{
    [Serializable]
    public class DialogueEntry
    {
        public DialogueGraph graph;
        public DialogueEventType eventType = DialogueEventType.None;
        public DialogueNpcState npcState = DialogueNpcState.None;
    }

    [Serializable]
    public class DialogueNpcGroup
    {
        public DialogueNpcId npcId;
        public List<DialogueEntry> entries = new List<DialogueEntry>();
    }

    /// <summary>
    /// Groups dialogue graphs per NPC and matches them by (event type, NPC state). Each exact
    /// match set is a play-once bag: every distinct graph plays once before random picking
    /// resumes. Played state is session-only and never serialized into the asset.
    /// </summary>
    [CreateAssetMenu(fileName = "DialogueCatalog", menuName = "Dialogue System/Dialogue Catalog")]
    public class DialogueCatalog : ScriptableObject
    {
        public List<DialogueNpcGroup> groups = new List<DialogueNpcGroup>();

        // Session-only bookkeeping keyed by "npcId|eventType|npcState".
        // A Dictionary is acceptable here only because it is not Unity-serialized.
        [System.NonSerialized] private Dictionary<string, HashSet<DialogueGraph>> playedGraphsBySet;

        /// <summary>
        /// Picks a graph for the NPC/event pair from the play-once bag. Every distinct graph in
        /// the exact match set plays once; when all have played the set resets and picking
        /// resumes. Returns null when no group or entry matches.
        /// </summary>
        public DialogueGraph SelectGraph(DialogueNpcId npcId, DialogueEventType eventType, DialogueNpcState npcState)
        {
            DialogueNpcGroup group = FindGroup(npcId);

            if(group == null || group.entries == null) return null;

            List<DialogueGraph> matched = new List<DialogueGraph>();

            foreach(DialogueEntry entry in group.entries)
            {
                if(!EntryMatches(entry, eventType, npcState)) continue;
                if(matched.Contains(entry.graph)) continue;
                matched.Add(entry.graph);
            }

            if(matched.Count == 0) return null;

            string key = BuildBagKey(npcId, eventType, npcState);
            HashSet<DialogueGraph> played = GetPlayedSet(key);

            List<DialogueGraph> unplayed = new List<DialogueGraph>();

            foreach(DialogueGraph graph in matched)
            {
                if(!played.Contains(graph)) unplayed.Add(graph);
            }

            if(unplayed.Count == 0)
            {
                // Whole set has played: reset that exact set and pick again.
                played.Clear();
                unplayed.AddRange(matched);
            }

            int index = UnityEngine.Random.Range(0, unplayed.Count);
            DialogueGraph selected = unplayed[index];
            played.Add(selected);
            return selected;
        }

        /// <summary>
        /// True when any authored entry matches the NPC/event pair. The play-once state is
        /// deliberately ignored here so the director's policy path still recognizes a set that
        /// has already been played.
        /// </summary>
        public bool HasRule(DialogueNpcId npcId, DialogueEventType eventType, DialogueNpcState npcState)
        {
            DialogueNpcGroup group = FindGroup(npcId);

            if(group == null || group.entries == null) return false;

            foreach(DialogueEntry entry in group.entries)
            {
                if(EntryMatches(entry, eventType, npcState)) return true;
            }

            return false;
        }

        /// <summary>Clears all session-only play-once bookkeeping.</summary>
        public void ResetPlayed()
        {
            if(playedGraphsBySet != null) playedGraphsBySet.Clear();
        }

        private DialogueNpcGroup FindGroup(DialogueNpcId npcId)
        {
            if(groups == null) return null;

            foreach(DialogueNpcGroup group in groups)
            {
                if(group == null) continue;
                if(group.npcId == npcId) return group;
            }

            return null;
        }

        private static bool EntryMatches(DialogueEntry entry, DialogueEventType eventType, DialogueNpcState npcState)
        {
            return entry != null
                && entry.graph != null
                && entry.eventType == eventType
                && entry.npcState == npcState;
        }

        private static string BuildBagKey(DialogueNpcId npcId, DialogueEventType eventType, DialogueNpcState npcState)
        {
            return $"{npcId}|{eventType}|{npcState}";
        }

        private HashSet<DialogueGraph> GetPlayedSet(string key)
        {
            if(playedGraphsBySet == null) playedGraphsBySet = new Dictionary<string, HashSet<DialogueGraph>>();

            HashSet<DialogueGraph> played;

            if(!playedGraphsBySet.TryGetValue(key, out played) || played == null)
            {
                played = new HashSet<DialogueGraph>();
                playedGraphsBySet[key] = played;
            }

            return played;
        }
    }
}
