#if UNITY_EDITOR
using UnityEngine.UIElements;

namespace bHapticsOSC.VRChat
{
    /// <summary>
    /// A titled run of steps that folds itself away once every step in it has been checked and
    /// come back fine.
    ///
    /// The rule is deliberately automatic rather than remembered-by-default: a group the user
    /// collapsed last week should not hide a problem that appeared today. A manual toggle is
    /// remembered, but only for as long as the state that prompted it lasts - reopening the
    /// window after fixing something starts from the clean view again.
    /// </summary>
    internal sealed class bStepGroupElement : VisualElement
    {
        internal bStepGroupElement(bSetupGroup group)
        {
            if (group == null || group.Steps.Count == 0)
                return;

            AddToClassList("b-group");

            bool clean = group.IsClean;
            string key = "group." + group.Id + (clean ? ".clean" : ".dirty");

            var foldout = new Foldout
            {
                text = group.Title,
                value = bUI.GetFlag(key, !clean),
            };
            foldout.RegisterValueChangedCallback(evt =>
            {
                if (evt.target == foldout)
                    bUI.SetFlag(key, evt.newValue);
            });

            // The roll-up is what makes collapsing safe: the header still answers the question
            // the group exists to answer, so nothing is hidden behind a triangle.
            var rollup = new Label(Describe(group));
            rollup.AddToClassList("b-group__rollup");
            rollup.pickingMode = PickingMode.Ignore;

            Toggle toggle = foldout.Q<Toggle>();
            VisualElement header = toggle?.Q(className: "unity-toggle__input") ?? toggle;
            header?.Add(rollup);

            var content = new VisualElement();
            content.AddToClassList("b-group__content");
            foreach (bSetupStep step in group.Steps)
                content.Add(new bStepRowElement(step));

            foldout.Add(content);
            Add(foldout);
        }

        private static string Describe(bSetupGroup group)
        {
            int blocked = 0;
            int attention = 0;
            int notChecked = 0;
            foreach (bSetupStep step in group.Steps)
            {
                if (step.State == bStepState.Blocked)
                    blocked++;
                else if (step.State == bStepState.Attention)
                    attention++;
                else if (step.State == bStepState.Unknown)
                    notChecked++;
            }

            // "all set" speaks for every step in the group, so it can only be said once every one
            // of them has actually been looked at. A step whose probe came back empty is reported
            // as its own quiet count rather than being added to the to-do numbers, because there
            // is nothing here for the user to go and do - and it only ever shows up when nothing
            // is wrong, so a real problem still owns the header.
            if (blocked == 0 && attention == 0)
            {
                if (notChecked == 0)
                    return "all set";

                return notChecked == 1 ? "1 unchecked" : notChecked + " unchecked";
            }

            if (blocked > 0 && attention > 0)
                return blocked + " to fix, " + attention + " to do";

            if (blocked > 0)
                return blocked == 1 ? "1 to fix" : blocked + " to fix";

            return attention == 1 ? "1 to do" : attention + " to do";
        }
    }
}
#endif
