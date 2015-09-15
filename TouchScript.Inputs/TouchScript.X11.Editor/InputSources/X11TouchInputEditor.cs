using TouchScript.InputSources;
using UnityEditor;

namespace TouchScript.Editor.InputSources
{
    [CustomEditor(typeof(X11TouchInput), true)]
    internal sealed class X11TouchInputEditor : InputSourceEditor
    {
        private SerializedProperty tags;

        protected override void OnEnable()
        {
            base.OnEnable();

            tags = serializedObject.FindProperty("Tags");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.UpdateIfDirtyOrScript();
            serializedObject.ApplyModifiedProperties();
            base.OnInspectorGUI();
        }

        protected override void drawAdvanced()
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(tags);
            EditorGUI.indentLevel--;
        }
    }
}
