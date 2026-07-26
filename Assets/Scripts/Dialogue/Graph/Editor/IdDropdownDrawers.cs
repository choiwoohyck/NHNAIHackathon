using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// [QuestionId]/[TestimonyId]/[SuspectId] 문자열 필드를 '사건 내 실제 id 목록'에서 고르는
// 드롭다운으로 표시한다. 오타/삭제된 id는 "⚠ … (미등록)"으로 눈에 띄게 보여준다.
// List<string> 필드에 붙이면 각 요소가 드롭다운이 된다(Unity 2020.1+).
public abstract class IdPopupDrawer : PropertyDrawer
{
    protected abstract List<string> GatherIds(InterrogationCase caseAsset);

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        // 문자열이 아니거나(예외) 컨텍스트가 InterrogationCase가 아니면 일반 필드로 폴백.
        if (property.propertyType != SerializedPropertyType.String)
        {
            EditorGUI.PropertyField(position, property, label);
            return;
        }
        var caseAsset = property.serializedObject.targetObject as InterrogationCase;
        if (caseAsset == null)
        {
            EditorGUI.PropertyField(position, property, label);
            return;
        }

        // 표시 문자열 / 실제 값 병렬 목록. 0번은 항상 (없음).
        var display = new List<string> { "(없음)" };
        var values = new List<string> { "" };
        foreach (var id in GatherIds(caseAsset))
        {
            if (string.IsNullOrEmpty(id) || values.Contains(id)) continue;
            display.Add(id);
            values.Add(id);
        }

        string current = property.stringValue ?? "";
        int index = values.IndexOf(current);
        if (index < 0)
        {
            // 목록에 없는 값(오타/삭제) → 경고 항목으로 노출하고 선택 상태 유지
            display.Insert(1, "⚠ " + current + " (미등록)");
            values.Insert(1, current);
            index = 1;
        }

        EditorGUI.BeginProperty(position, label, property);
        int newIndex = EditorGUI.Popup(position, label.text, index, display.ToArray());
        if (newIndex >= 0 && newIndex < values.Count && values[newIndex] != current)
            property.stringValue = values[newIndex];
        EditorGUI.EndProperty();
    }
}

[CustomPropertyDrawer(typeof(QuestionIdAttribute))]
public class QuestionIdDrawer : IdPopupDrawer
{
    protected override List<string> GatherIds(InterrogationCase c)
    {
        var ids = new List<string>();
        if (c.suspects != null)
            foreach (var s in c.suspects)
                if (s != null && s.questions != null)
                    foreach (var q in s.questions)
                        if (q != null && !string.IsNullOrEmpty(q.id)) ids.Add(q.id);
        return ids;
    }
}

[CustomPropertyDrawer(typeof(TestimonyIdAttribute))]
public class TestimonyIdDrawer : IdPopupDrawer
{
    protected override List<string> GatherIds(InterrogationCase c)
    {
        var ids = new List<string>();
        if (c.testimonies != null)
            foreach (var t in c.testimonies)
                if (t != null && !string.IsNullOrEmpty(t.id)) ids.Add(t.id);
        return ids;
    }
}

[CustomPropertyDrawer(typeof(SuspectIdAttribute))]
public class SuspectIdDrawer : IdPopupDrawer
{
    protected override List<string> GatherIds(InterrogationCase c)
    {
        var ids = new List<string>();
        if (c.suspects != null)
            foreach (var s in c.suspects)
                if (s != null && !string.IsNullOrEmpty(s.id)) ids.Add(s.id);
        return ids;
    }
}
