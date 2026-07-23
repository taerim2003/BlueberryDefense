using System;
using System.Collections.Generic;
using UnityEngine;

// 지속시간이 있는 플레이어 버프를 HUD에 표시하기 위한 범용 레지스트리.
// 버프를 발생시키는 쪽(PlayerSkills, OrbAltar 등)이 Set/Clear만 호출하면 되고,
// HUD는 GetActive()로 목록을 받아 아이콘/슬롯에 채우기만 하면 되므로 새 버프가 늘어나도 양쪽 다 손댈 곳이 최소화된다.
public static class BuffTracker
{
    public class Entry
    {
        public string Key;
        public float EndTime;
        public Func<int> StackCount; // null이면 스택 표시 없음
        public bool ShowTimer = true; // false면 HUD에 남은시간 텍스트를 숨기고 스택만 표시
    }

    private static readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();

    public static void Set(string key, float endTime, Func<int> stackCount = null, bool showTimer = true)
    {
        entries[key] = new Entry { Key = key, EndTime = endTime, StackCount = stackCount, ShowTimer = showTimer };
    }

    public static void Clear(string key) => entries.Remove(key);

    // 판 시작 시 지난 판의 버프 표시가 남아 있지 않도록 비운다 (RunState에서 호출)
    public static void ResetRunState() => entries.Clear();

    public static List<Entry> GetActive()
    {
        List<Entry> result = new List<Entry>();
        foreach (Entry e in entries.Values)
            if (Time.time < e.EndTime) result.Add(e);
        return result;
    }
}
