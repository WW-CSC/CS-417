using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

// Names are prefixed (Threshold*) so they don't clash with teammates' scripts.

public enum ThresholdConditionType
{
    ResourceAtLeast,   // key = resource name (e.g. "Stone"), amount = how many the player holds right now
    CounterAtLeast     // key = counter name (e.g. "Smelted"), amount = how many times it was counted
}

[Serializable]
public class ThresholdCondition
{
    public ThresholdConditionType type;
    public string key;
    public int amount = 1;
}

[Serializable]
public class ThresholdDef
{
    public string id = "Prospector";

    [Tooltip("Ids of thresholds that must be cleared before this one is even checked.")]
    public List<string> prerequisites = new List<string>();

    [Tooltip("ALL of these must be true to clear the threshold.")]
    public List<ThresholdCondition> conditions = new List<ThresholdCondition>();

    [Tooltip("Shown on the goal panel once this threshold becomes the next thing to do.")]
    [TextArea] public string goalText;

    [Header("Unlock Reveal card (shown before the new content appears)")]
    public string revealTitle;
    [TextArea] public string revealText;      // should explain the new mechanic's interface metaphor

    [Header("Content hidden until this threshold is cleared")]
    public GameObject[] revealObjects;

    public UnityEvent onCleared;              // optional: hook sandboxes, sounds, etc.

    [NonSerialized] public bool cleared;
}

[DefaultExecutionOrder(-100)]   // runs before other scripts so hidden content is hidden before it starts
public class ThresholdManager : MonoBehaviour
{
    public static ThresholdManager Instance { get; private set; }

    public List<ThresholdDef> thresholds = new List<ThresholdDef>();

    [Header("Unlock Reveal card")]
    public GameObject revealCard;
    public TMP_Text revealTitleLabel;
    public TMP_Text revealBodyLabel;
    public float revealSeconds = 4f;

    [Header("Thresholds on the Horizon")]
    public GameObject goalPanel;     // hidden until the first threshold is cleared
    public TMP_Text goalLabel;

    readonly Dictionary<string, int> counters = new Dictionary<string, int>();
    readonly Queue<ThresholdDef> revealQueue = new Queue<ThresholdDef>();
    bool revealing;
    float nextCheck;

    void Awake()
    {
        Instance = this;

        foreach (var t in thresholds)
        {
            t.cleared = false;
            foreach (var go in t.revealObjects)
                if (go != null) go.SetActive(false);
        }

        if (revealCard != null) revealCard.SetActive(false);
        if (goalPanel != null) goalPanel.SetActive(false);
    }

    void Start()
    {
        // Catch typos in prerequisite ids early.
        foreach (var t in thresholds)
            foreach (var p in t.prerequisites)
                if (!thresholds.Exists(x => x.id == p))
                    Debug.LogError($"Threshold '{t.id}' lists unknown prerequisite '{p}'.", this);
    }

    // ---------- Counters (call from other scripts) ----------

    public void AddCount(string counterName, int n = 1)
    {
        counters.TryGetValue(counterName, out var c);
        counters[counterName] = c + n;
    }

    public int GetCount(string counterName) =>
        counters.TryGetValue(counterName, out var c) ? c : 0;

    // Safe to call even if there's no manager in the scene.
    public static void Count(string counterName, int n = 1)
    {
        if (Instance != null) Instance.AddCount(counterName, n);
    }

    public bool IsCleared(string id)
    {
        var t = thresholds.Find(x => x.id == id);
        return t != null && t.cleared;
    }

    // ---------- Checking ----------

    void Update()
    {
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + 0.25f;

        foreach (var t in thresholds)
        {
            // A child threshold isn't assessed until its parents are cleared.
            if (t.cleared || !PrerequisitesMet(t) || !ConditionsMet(t)) continue;

            t.cleared = true;          // latches: spending the resources later won't un-clear it
            revealQueue.Enqueue(t);
        }

        if (!revealing && revealQueue.Count > 0) StartCoroutine(RunReveals());
    }

    bool PrerequisitesMet(ThresholdDef t)
    {
        foreach (var p in t.prerequisites)
            if (!IsCleared(p)) return false;
        return true;
    }

    bool ConditionsMet(ThresholdDef t)
    {
        if (t.conditions.Count == 0) return false;   // an empty threshold never clears by accident

        foreach (var c in t.conditions)
        {
            int have = c.type == ThresholdConditionType.ResourceAtLeast
                ? ResourceManager.Instance.Get(c.key)
                : GetCount(c.key);

            if (have < c.amount) return false;
        }
        return true;
    }

    // ---------- Reveal, then unlock, then show the next goal ----------

    IEnumerator RunReveals()
    {
        revealing = true;

        while (revealQueue.Count > 0)
        {
            var t = revealQueue.Dequeue();

            // 1. Unlock Reveal card first (introduces the metaphor)...
            if (revealCard != null && !string.IsNullOrEmpty(t.revealText))
            {
                if (revealTitleLabel != null) revealTitleLabel.text = t.revealTitle;
                if (revealBodyLabel != null) revealBodyLabel.text = t.revealText;
                revealCard.SetActive(true);
                yield return new WaitForSeconds(revealSeconds);
                revealCard.SetActive(false);
            }

            // 2. ...then the new prefabs / UI / pop-ups appear.
            foreach (var go in t.revealObjects)
                if (go != null) go.SetActive(true);

            t.onCleared.Invoke();

            // 3. Thresholds on the Horizon: show what to do next.
            UpdateGoals();
        }

        revealing = false;
    }

    void UpdateGoals()
    {
        if (goalLabel == null) return;

        var lines = new List<string>();
        bool anyCleared = false;
        bool anyPending = false;

        foreach (var t in thresholds)
        {
            if (t.cleared) { anyCleared = true; continue; }
            anyPending = true;
            if (PrerequisitesMet(t) && !string.IsNullOrEmpty(t.goalText))
                lines.Add("• " + t.goalText);
        }

        if (!anyCleared) return;   // the horizon only appears AFTER a threshold has been cleared

        goalLabel.text = anyPending
            ? "Next goals:\n" + string.Join("\n", lines)
            : "All goals complete!";

        if (goalPanel != null) goalPanel.SetActive(true);
    }
}
