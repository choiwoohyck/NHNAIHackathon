using UnityEngine;

// 인스펙터에서 문자열 id 필드를 '드롭다운'으로 그리기 위한 마커 애트리뷰트.
// 실제 목록 표시는 Editor/IdDropdownDrawers.cs 의 PropertyDrawer가 담당한다(런타임 동작 없음).
//
// List<string> 필드에 붙이면 각 요소가 드롭다운이 된다(Unity 2020.1+).
public class QuestionIdAttribute : PropertyAttribute { }   // 사건 내 질문 id 목록에서 선택
public class TestimonyIdAttribute : PropertyAttribute { }  // 사건 내 증언 id 목록에서 선택
public class SuspectIdAttribute : PropertyAttribute { }    // 사건 내 용의자 id 목록에서 선택
