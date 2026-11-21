using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityDriver.Characters
{
    public enum CharPose { Idle, Walk, Sit, Talk, Aim, HandsUp, Wave, Drive }

    /// <summary>Procedural animation for the rigid-part character rig (no animation clips): swings, sits, talks, aims, raises hands.</summary>
    public sealed class CharacterAnimator : MonoBehaviour
    {
        public CharPose pose = CharPose.Idle;
        public float speed = 1f;
        private Transform hips, torso, head, shL, shR, elL, elR, hpL, hpR, knL, knR, gun;
        private Vector3 hipsRest;
        private float phase;

        private void Awake()
        {
            hips = CharacterCustomizer.FindDeep(transform, "Hips"); torso = CharacterCustomizer.FindDeep(transform, "Torso"); head = CharacterCustomizer.FindDeep(transform, "Head");
            shL = CharacterCustomizer.FindDeep(transform, "ShoulderL"); shR = CharacterCustomizer.FindDeep(transform, "ShoulderR");
            elL = CharacterCustomizer.FindDeep(transform, "ElbowL"); elR = CharacterCustomizer.FindDeep(transform, "ElbowR");
            hpL = CharacterCustomizer.FindDeep(transform, "HipL"); hpR = CharacterCustomizer.FindDeep(transform, "HipR");
            knL = CharacterCustomizer.FindDeep(transform, "KneeL"); knR = CharacterCustomizer.FindDeep(transform, "KneeR");
            gun = CharacterCustomizer.FindDeep(transform, "Pistol");
            if (hips != null) hipsRest = hips.localPosition;
            phase = UnityEngine.Random.value * 10f;
        }

        private static Quaternion X(float forwardDegrees, float roll = 0f, float yaw = 0f) => Quaternion.Euler(-forwardDegrees, yaw, roll);

        private float clock;
        private void Update() { Tick(Time.deltaTime); }

        /// <summary>Advances the pose by <paramref name="deltaTime"/> seconds (the preview calls this with unscaled time while a menu pauses the game).</summary>
        public void Tick(float deltaTime)
        {
            if (hips == null) return;
            clock += deltaTime;
            float t = (clock + phase) * speed, dt = Mathf.Min(1f, deltaTime * 14f);
            Vector3 hp = hipsRest; float lean = 0f;
            Quaternion sL = Quaternion.identity, sR = Quaternion.identity, eL = Quaternion.identity, eR = Quaternion.identity, tL = Quaternion.identity, tR = Quaternion.identity, kL = Quaternion.identity, kR = Quaternion.identity;
            Quaternion headRot = Quaternion.identity;
            switch (pose)
            {
                case CharPose.Walk:
                {
                    float a = Mathf.Sin(t * 6f);
                    tL = X(a * 30f); tR = X(-a * 30f); kL = X(Mathf.Max(0f, -a) * 45f); kR = X(Mathf.Max(0f, a) * 45f);
                    sL = X(-a * 24f); sR = X(a * 24f); eL = X(12f); eR = X(12f); hp.y += Mathf.Abs(Mathf.Cos(t * 6f)) * 0.025f - 0.012f; lean = 3f;
                    break;
                }
                case CharPose.Sit:
                    hp.y = hipsRest.y - 0.46f; tL = tR = X(88f); kL = kR = X(-88f); sL = sR = X(28f); eL = eR = X(40f); break;
                case CharPose.Drive:
                    hp.y = hipsRest.y - 0.46f; tL = tR = X(88f); kL = kR = X(-88f); sL = X(58f, 0f, 8f); sR = X(58f, 0f, -8f); eL = eR = X(30f); headRot = Quaternion.Euler(Mathf.Sin(t * 0.8f) * 2f, Mathf.Sin(t * 0.5f) * 6f, 0f); break;
                case CharPose.Talk:
                {
                    float g = Mathf.Sin(t * 3.1f);
                    sR = X(35f + g * 18f, 0f, -10f); eR = X(70f + Mathf.Sin(t * 4.3f) * 20f); sL = X(6f); eL = X(18f); headRot = Quaternion.Euler(Mathf.Sin(t * 5f) * 3f, Mathf.Sin(t * 1.7f) * 8f, 0f); break;
                }
                case CharPose.Aim:
                    sR = X(88f, 0f, -4f); eR = X(6f); sL = X(80f, 0f, 22f); eL = X(48f); tL = X(8f); tR = X(-12f); hp.y -= 0.05f; kL = X(10f); kR = X(10f); lean = 5f; break;
                case CharPose.HandsUp:
                    sL = X(160f, 0f, 14f); sR = X(160f, 0f, -14f); eL = X(25f); eR = X(25f); headRot = Quaternion.Euler(-4f, 0f, 0f); break;
                case CharPose.Wave:
                    sR = X(150f + Mathf.Sin(t * 9f) * 12f, 0f, -20f); eR = X(40f + Mathf.Sin(t * 9f) * 25f); sL = X(4f); eL = X(14f); break;
                default:
                    sL = X(4f + Mathf.Sin(t * 1.1f) * 2f); sR = X(4f - Mathf.Sin(t * 1.1f) * 2f); eL = X(10f); eR = X(10f); hp.y += Mathf.Sin(t * 1.6f) * 0.004f; headRot = Quaternion.Euler(0f, Mathf.Sin(t * 0.35f) * 10f, 0f); break;
            }
            hips.localPosition = Vector3.Lerp(hips.localPosition, hp, dt);
            if (torso != null) torso.localRotation = Quaternion.Slerp(torso.localRotation, Quaternion.Euler(lean, 0f, 0f), dt);
            if (head != null) head.localRotation = Quaternion.Slerp(head.localRotation, headRot, dt);
            Slerp(shL, sL, dt); Slerp(shR, sR, dt); Slerp(elL, eL, dt); Slerp(elR, eR, dt); Slerp(hpL, tL, dt); Slerp(hpR, tR, dt); Slerp(knL, kL, dt); Slerp(knR, kR, dt);
            if (gun != null) gun.gameObject.SetActive(pose == CharPose.Aim && CharacterHolds.Pistol(this));
        }

        private static void Slerp(Transform t, Quaternion q, float k) { if (t != null) t.localRotation = Quaternion.Slerp(t.localRotation, q, k); }
    }

    /// <summary>Whether a character carries a pistol (set by the role preset: police, SWAT, gang).</summary>
    public static class CharacterHolds
    {
        public static bool Pistol(CharacterAnimator a)
        {
            var c = a.GetComponent<CharacterCustomizer>();
            return c != null && c.Look != null && c.Look.Has("Pistol");
        }
    }
}
