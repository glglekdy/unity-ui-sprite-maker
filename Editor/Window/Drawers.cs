using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace UISpriteMaker.Editor
{
    /// <summary>Shows only the fields the selected fill mode uses.</summary>
    [CustomPropertyDrawer(typeof(FillSettings))]
    sealed class FillSettingsDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new Foldout { text = property.displayName, value = true };
            var mode = new PropertyField(property.FindPropertyRelative(nameof(FillSettings.mode)));
            var color = new PropertyField(property.FindPropertyRelative(nameof(FillSettings.color)));
            var gradient = new PropertyField(property.FindPropertyRelative(nameof(FillSettings.gradient)));
            var angle = new PropertyField(property.FindPropertyRelative(nameof(FillSettings.angle)));
            var center = new PropertyField(property.FindPropertyRelative(nameof(FillSettings.radialCenter)));
            var radius = new PropertyField(property.FindPropertyRelative(nameof(FillSettings.radialRadius)));
            foreach (var e in new VisualElement[] { mode, color, gradient, angle, center, radius }) root.Add(e);

            void Refresh(FillMode m)
            {
                color.style.display = m == FillMode.Solid ? DisplayStyle.Flex : DisplayStyle.None;
                gradient.style.display = m != FillMode.Solid ? DisplayStyle.Flex : DisplayStyle.None;
                angle.style.display = m == FillMode.LinearGradient ? DisplayStyle.Flex : DisplayStyle.None;
                center.style.display = radius.style.display = m == FillMode.RadialGradient ? DisplayStyle.Flex : DisplayStyle.None;
            }

            var modeProp = property.FindPropertyRelative(nameof(FillSettings.mode));
            Refresh((FillMode)modeProp.enumValueIndex);
            mode.RegisterValueChangeCallback(e => Refresh((FillMode)e.changedProperty.enumValueIndex));
            return root;
        }
    }

    /// <summary>One radius, or four when the corners are unlinked.</summary>
    [CustomPropertyDrawer(typeof(CornerRadius))]
    sealed class CornerRadiusDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new Foldout { text = property.displayName, value = true };
            var link = new PropertyField(property.FindPropertyRelative(nameof(CornerRadius.linkCorners)));
            var all = new PropertyField(property.FindPropertyRelative(nameof(CornerRadius.radius)));
            var corners = new VisualElement();
            corners.Add(new PropertyField(property.FindPropertyRelative(nameof(CornerRadius.topLeft))));
            corners.Add(new PropertyField(property.FindPropertyRelative(nameof(CornerRadius.topRight))));
            corners.Add(new PropertyField(property.FindPropertyRelative(nameof(CornerRadius.bottomRight))));
            corners.Add(new PropertyField(property.FindPropertyRelative(nameof(CornerRadius.bottomLeft))));
            root.Add(link);
            root.Add(all);
            root.Add(corners);

            void Refresh(bool linked)
            {
                all.style.display = linked ? DisplayStyle.Flex : DisplayStyle.None;
                corners.style.display = linked ? DisplayStyle.None : DisplayStyle.Flex;
            }

            Refresh(property.FindPropertyRelative(nameof(CornerRadius.linkCorners)).boolValue);
            link.RegisterValueChangeCallback(e => Refresh(e.changedProperty.boolValue));
            return root;
        }
    }
}
