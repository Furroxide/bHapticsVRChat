#if UNITY_EDITOR
using UnityEngine.UIElements;

namespace bHapticsOSC.VRChat
{
    /// <summary>
    /// One row of the setup panel.
    ///
    /// The whole redesign lives in this class. A satisfied step is a single line - marker, title,
    /// dim value - and its prose is reachable only by hovering. A step that needs something
    /// becomes the opposite: a tinted card with a coloured edge, one short sentence, and its
    /// buttons right there. That difference is what makes a healthy panel quiet and an unhealthy
    /// one obvious, and it is exactly what the old shared help-box could not express.
    /// </summary>
    internal sealed class bStepRowElement : VisualElement
    {
        internal bStepRowElement(bSetupStep step)
        {
            AddToClassList("b-step");
            AddToClassList("b-step--" + bUI.StateClass(step.State));
            if (!string.IsNullOrEmpty(step.Id))
                AddToClassList("b-step--id-" + step.Id);

            // The long form is always reachable by hover, whatever the state. Nothing that used
            // to be on screen has been thrown away; it has stopped being shouted.
            if (!string.IsNullOrEmpty(step.Explanation))
                tooltip = step.Explanation;

            Add(BuildHead(step));

            if (!step.NeedsAttention)
                return;

            if (!string.IsNullOrEmpty(step.Detail))
            {
                var detail = new Label(step.Detail);
                detail.AddToClassList("b-step__detail");
                Add(detail);
            }

            VisualElement actions = BuildActions(step);
            if (actions != null)
                Add(actions);

            if (!string.IsNullOrEmpty(step.Explanation))
                Add(BuildWhy(step));
        }

        private static VisualElement BuildHead(bSetupStep step)
        {
            var head = new VisualElement();
            head.AddToClassList("b-step__head");
            head.Add(bUI.CreateStateMarker(step.State, "b-step__icon"));

            var title = new Label(step.Title);
            title.AddToClassList("b-step__title");
            head.Add(title);

            // Only a satisfied row carries a value: on an unhappy row the short Detail sentence
            // below is doing that job, and two competing summaries read worse than one.
            if (!step.NeedsAttention)
            {
                if (!string.IsNullOrEmpty(step.Value))
                {
                    var value = new Label(step.Value);
                    value.AddToClassList("b-step__value");
                    head.Add(value);
                }

                // A satisfied step can still offer something - "set this avatar up again" is the
                // case that matters. It goes inline so the row stays one line high; expanding a
                // healthy row to make space for an optional button would undo the point.
                Button inline = BuildInlineAction(step);
                if (inline != null)
                    head.Add(inline);
            }

            return head;
        }

        private static Button BuildInlineAction(bSetupStep step)
        {
            if (step.Actions == null || step.Actions.Length == 0)
                return null;

            bStepAction action = step.Actions[0];
            foreach (bStepAction candidate in step.Actions)
            {
                if (candidate.IsPrimary)
                {
                    action = candidate;
                    break;
                }
            }

            if (!action.Enabled)
                return null;

            var button = new Button(() => action.Run?.Invoke()) { text = action.Label };
            button.AddToClassList("b-step__inline-action");
            return button;
        }

        private static VisualElement BuildActions(bSetupStep step)
        {
            if (step.Actions == null || step.Actions.Length == 0)
                return null;

            var row = new VisualElement();
            row.AddToClassList("b-step__actions");

            foreach (bStepAction action in step.Actions)
            {
                bStepAction captured = action;
                var button = new Button(() => captured.Run?.Invoke()) { text = captured.Label };
                button.SetEnabled(captured.Enabled);
                if (captured.IsPrimary)
                    button.AddToClassList("b-step__action--primary");

                row.Add(button);
            }

            return row;
        }

        /// <summary>
        /// Where the paragraph went. Collapsed by default and remembered per step for the session,
        /// so reading it once does not mean reading it on every repaint.
        /// </summary>
        private static VisualElement BuildWhy(bSetupStep step)
        {
            string key = "details." + step.Id;
            var why = new Foldout
            {
                text = "Why this matters",
                value = bUI.GetFlag(key, false),
            };
            why.AddToClassList("b-step__why");

            var explanation = new Label(step.Explanation);
            explanation.AddToClassList("b-step__explanation");
            why.Add(explanation);

            why.RegisterValueChangedCallback(evt =>
            {
                if (evt.target == why)
                    bUI.SetFlag(key, evt.newValue);
            });

            return why;
        }
    }
}
#endif
