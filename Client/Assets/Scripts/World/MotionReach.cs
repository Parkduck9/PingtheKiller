using UnityEngine;

namespace PingTheKiller.Client.World
{
    // ButtonPushing at a terminal: the clip presses at chest height, so while Weight > 0 the right hand's IK goal moves
    // ButtonDrop down (never below ButtonFloor above the character root), keeping the clip's own press motion. Needs the
    // IK pass on Motion.controller's base layer. ExecuteAlways: editor renders and EditMode tests run the IK too.
    [ExecuteAlways]
    public sealed class MotionReach : MonoBehaviour
    {
        public float Weight { get; set; }
        private Animator animator;

        private void OnAnimatorIK(int layer)
        {
            if (layer != 0 || Weight <= 0) return;
            if (animator == null) animator = GetComponent<Animator>();
            Transform space = transform.parent;
            if (animator == null || space == null) return;
            Vector3 hand = animator.GetIKPosition(AvatarIKGoal.RightHand);
            float height = space.InverseTransformPoint(hand).y;
            float goal = Mathf.Max(MotionTiming.ButtonFloor, height - MotionTiming.ButtonDrop);
            if (goal >= height) return;
            animator.SetIKPosition(AvatarIKGoal.RightHand, hand + space.up * (goal - height));
            animator.SetIKPositionWeight(AvatarIKGoal.RightHand, Weight);
        }
    }
}
