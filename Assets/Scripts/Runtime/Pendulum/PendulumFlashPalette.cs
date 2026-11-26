using UnityEngine;

namespace Taurus.Runtime.Pendulum
{
    /// <summary>
    /// 真上通過時の発光色を RulesPanel デモ（RuleDemoAnimator）と本番振り子（PendulumVisualFeedback）で
    /// 一元管理するための共有パレット。片方だけ色を直して食い違う事故を防ぐ。
    /// 視認性改善のため、発光色は彩度・輝度を高めに設定している。
    /// </summary>
    public static class PendulumFlashPalette
    {
        public static readonly Color ParentBaseColor = new Color(0.22f, 0.34f, 0.31f, 1f);
        public static readonly Color ParentFlashColor = new Color(0.90f, 1.0f, 0.10f, 1f); // 高彩度ライム
        public static readonly Color ChildBaseColor = new Color(0.36f, 0.27f, 0.24f, 1f);
        public static readonly Color ChildFlashColor = new Color(1.0f, 0.22f, 0.05f, 1f); // 高彩度オレンジ
        public static readonly Color GrandchildBaseColor = new Color(0.20f, 0.30f, 0.38f, 1f);
        public static readonly Color GrandchildFlashColor = new Color(0.10f, 0.92f, 1.0f, 1f); // 高彩度シアン（三重振り子・孫玉）
    }
}
