using System;
using System.Collections.Generic;
using Taurus.Runtime.Pendulum;
using Taurus.Runtime.Stage;
using UnityEditor;
using UnityEngine;

namespace Taurus.Editor.Inspectors
{
    /// <summary>
    /// StageDefinition のカスタム Inspector。
    /// 数理モデル（区間密度 D_k、区分線形補間＋台形積分平均、L2ピーク評価）および
    /// 前向きDP（厳密確率分布・制約達成確率・条件付き期待量）／後ろ向きDP（到達可能性・ランタイム検証）を
    /// リアルタイムにグラフと数値で可視化する。
    /// </summary>
    [CustomEditor(typeof(StageDefinition))]
    public class StageDefinitionEditor : UnityEditor.Editor
    {
        private static readonly Color GRAPH_BG = new Color(0.10f, 0.14f, 0.14f, 1f);
        private static readonly Color GRID_COLOR = new Color(1f, 1f, 1f, 0.10f);
        private static readonly Color AVG_LINE_COLOR = new Color(0.35f, 0.82f, 1.0f, 1f);
        private static readonly Color L2_LINE_COLOR = new Color(0.835f, 1.0f, 0.345f, 1f);
        private static readonly Color SCORE_LINE_COLOR = new Color(1.0f, 0.451f, 0.357f, 1f);
        private static readonly Color IN_RANGE_BAR_COLOR = new Color(0.835f, 1.0f, 0.345f, 0.85f);
        private static readonly Color OUT_RANGE_BAR_COLOR = new Color(0.45f, 0.50f, 0.50f, 0.45f);
        private static readonly Color INTERVAL_BAND_A = new Color(1f, 1f, 1f, 0.03f);
        private static readonly Color INTERVAL_BAND_B = new Color(1f, 1f, 1f, 0.08f);

        private int _selectedWaveIndex;
        private int _simSeed = 1000;
        private string _simSummary = string.Empty;
        private bool _showRawInspector = true;

        public override void OnInspectorGUI()
        {
            StageDefinition def = (StageDefinition)target;
            serializedObject.Update();

            DrawOverviewSection(def);
            EditorGUILayout.Space(8f);
            DrawSelectedWaveAnalysis(def);
            EditorGUILayout.Space(8f);
            DrawUtilityButtons(def);
            EditorGUILayout.Space(8f);

            _showRawInspector = EditorGUILayout.Foldout(_showRawInspector, "Wave・区間パラメータ編集（Raw Inspector）", true, EditorStyles.foldoutHeader);
            if (_showRawInspector)
            {
                DrawDefaultInspector();
            }

            if (serializedObject.ApplyModifiedProperties())
            {
                _simSummary = string.Empty;
                Repaint();
            }
        }

        private void DrawOverviewSection(StageDefinition def)
        {
            EditorGUILayout.LabelField("難易度評価ダッシュボード（数理・DPモデル）", EditorStyles.boldLabel);

            IReadOnlyList<WaveConfig> waves = def.Waves;
            int count = waves.Count;
            if (count == 0)
            {
                EditorGUILayout.HelpBox("Wave 定義が空です。", MessageType.Warning);
                return;
            }

            WaveDifficultyReport[] reports = new WaveDifficultyReport[count];
            float maxY = 0.5f;
            bool anyInfeasible = false;

            for (int i = 0; i < count; i++)
            {
                reports[i] = StageDifficultyEvaluator.EvaluateWave(
                    waves[i],
                    def.PeakWeight,
                    def.MultiPendulumPenalty,
                    def.DoublePendulumPenalty);
                maxY = Mathf.Max(maxY, (float)reports[i].BlendedDifficultyScore, (float)reports[i].L2RmsDensity, (float)reports[i].PeakDensity);
                if (!reports[i].AllConstraintsFeasible)
                {
                    anyInfeasible = true;
                }
            }

            if (anyInfeasible)
            {
                EditorGUILayout.HelpBox("到達不能な量制約 [Q_min, Q_max] を含む Wave があります。下の詳細で赤表示の区間を確認してください。", MessageType.Error);
            }

            // 全 Wave 横断の難易度推移グラフ
            Rect rect = GUILayoutUtility.GetRect(10f, 140f, GUILayout.ExpandWidth(true));
            DrawWaveProgressionGraph(rect, reports, maxY);

            // 凡例
            EditorGUILayout.BeginHorizontal();
            DrawLegendChip("平均密度(台形)", AVG_LINE_COLOR);
            DrawLegendChip("L2ピーク密度", L2_LINE_COLOR);
            DrawLegendChip("総合スコア(玉数込)", SCORE_LINE_COLOR);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSelectedWaveAnalysis(StageDefinition def)
        {
            IReadOnlyList<WaveConfig> waves = def.Waves;
            if (waves.Count == 0)
            {
                return;
            }

            _selectedWaveIndex = Mathf.Clamp(_selectedWaveIndex, 0, waves.Count - 1);

            string[] tabs = new string[waves.Count];
            for (int i = 0; i < waves.Count; i++)
            {
                tabs[i] = $"W{i + 1}";
            }

            int nextIndex = GUILayout.Toolbar(_selectedWaveIndex, tabs);
            if (nextIndex != _selectedWaveIndex)
            {
                _selectedWaveIndex = nextIndex;
                _simSummary = string.Empty;
            }

            WaveConfig wave = waves[_selectedWaveIndex];
            WaveDifficultyReport report = StageDifficultyEvaluator.EvaluateWave(
                wave,
                def.PeakWeight,
                def.MultiPendulumPenalty,
                def.DoublePendulumPenalty);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                $"Wave {_selectedWaveIndex + 1} 詳細（単:{wave.SinglePendulumCount} / 二重:{wave.DoublePendulumCount} / 総玉数:{report.TotalBobCount} / 構成負荷倍率:×{report.StructureDifficultyMultiplier:0.00} / 終了:{wave.StopRule}）",
                EditorStyles.boldLabel);

            if (!report.AllConstraintsFeasible)
            {
                EditorGUILayout.HelpBox("後ろ向きDP: can_reach[0, 0] = false の区間があります。この制約範囲は時間・候補からは達成不可能です。", MessageType.Error);
            }

            // 全体指標のサマリー
            EditorGUILayout.LabelField(
                $"期間: [{report.StartTime:0.0}, {report.EndTime:0.0}]s  |  期待量(1玉): {report.TotalExpectedQuantityPerBob:0.00}  |  期待量(全玉): {report.TotalExpectedQuantityAllBobs:0.00}");
            EditorGUILayout.LabelField(
                $"平均密度(台形積分): {report.TrapezoidalAverageDensity:0.000}/s  |  L2 RMS密度: {report.L2RmsDensity:0.000}/s  |  ピーク密度: {report.PeakDensity:0.000}/s");
            EditorGUILayout.LabelField(
                $"総合難易度スコア（peakWeight={def.PeakWeight:0.00}）: {report.BlendedDifficultyScore:0.000}",
                EditorStyles.boldLabel);

            // 区間難易度曲線 f(x) のグラフ
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("区間密度 D_k と区分線形難易度曲線 f(x)", EditorStyles.miniBoldLabel);
            Rect curveRect = GUILayoutUtility.GetRect(10f, 120f, GUILayout.ExpandWidth(true));
            DrawIntervalCurveGraph(curveRect, report);

            // 各区間の前向きDP・後ろ向きDP 詳細＋確率分布ヒストグラム
            for (int k = 0; k < report.Intervals.Count; k++)
            {
                IntervalDpResult iv = report.Intervals[k];
                EditorGUILayout.Space(4f);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                string constraintText = iv.UseQuantityConstraint
                    ? $"制約 [{iv.MinQuantity}, {iv.MaxQuantity}]  達成確率 P={iv.ConstraintSatisfactionProbability * 100.0:0.1}%  can_reach[0,0]={(iv.HasValidPath ? "OK" : "NG!")}"
                    : "制約なし";

                GUIStyle titleStyle = new GUIStyle(EditorStyles.miniBoldLabel);
                if (!iv.HasValidPath)
                {
                    titleStyle.normal.textColor = SCORE_LINE_COLOR;
                }

                EditorGUILayout.LabelField(
                    $"区間 {k + 1}「{iv.Label}」 [{iv.StartTime:0.0}〜{iv.EndTime:0.0}s, L={iv.DurationSeconds:0.0}s]  |  {constraintText}",
                    titleStyle);
                EditorGUILayout.LabelField(
                    $"密度 D={iv.Density:0.000}/s  |  無制約E[Q]={iv.UnconstrainedExpectedQuantity:0.00}  |  条件付E[Q]={iv.ConstrainedExpectedQuantity:0.00}  |  分散={iv.QuantityVariance:0.00}  |  RMS={iv.RmsQuantity:0.00}",
                    EditorStyles.miniLabel);

                Rect histRect = GUILayoutUtility.GetRect(10f, 64f, GUILayout.ExpandWidth(true));
                DrawProbabilityHistogram(histRect, iv);

                EditorGUILayout.EndVertical();
            }

            // ランタイム後ろ向きDP抽選の実測検証
            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginHorizontal();
            _simSeed = EditorGUILayout.IntField("検証シード", _simSeed);
            if (GUILayout.Button("後ろ向きDP 100回生成テスト", GUILayout.Width(190f)))
            {
                _simSummary = RunBackwardDpSimulation(wave, report, (uint)_simSeed, 100);
            }

            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_simSummary))
            {
                EditorGUILayout.HelpBox(_simSummary, MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawUtilityButtons(StageDefinition def)
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("デフォルト Wave 1〜8 で初期化"))
            {
                Undo.RecordObject(def, "Reset StageDefinition to Default Waves");
                def.ResetToDefaultWaves();
                EditorUtility.SetDirty(def);
                serializedObject.Update();
                _simSummary = string.Empty;
            }

            EditorGUILayout.EndHorizontal();
        }

        private static string RunBackwardDpSimulation(WaveConfig wave, WaveDifficultyReport report, uint baseSeed, int trials)
        {
            int minTotal = int.MaxValue;
            int maxTotal = int.MinValue;
            double sumTotal = 0.0;
            int satisfiedCount = 0;

            bool singleIntervalConstraint = report.Intervals.Count == 1 && report.Intervals[0].UseQuantityConstraint;
            int minQ = singleIntervalConstraint ? report.Intervals[0].MinQuantity : 0;
            int maxQ = singleIntervalConstraint ? report.Intervals[0].MaxQuantity : int.MaxValue;

            for (int i = 0; i < trials; i++)
            {
                MotionSchedule schedule = MotionScheduleGenerator.Generate(unchecked(baseSeed + (uint)(i * 7919)), wave);
                int q = schedule.TotalExpectedTopCount;
                if (q < minTotal) minTotal = q;
                if (q > maxTotal) maxTotal = q;
                sumTotal += q;

                if (!singleIntervalConstraint || (q >= minQ && q <= maxQ))
                {
                    satisfiedCount++;
                }
            }

            double avg = sumTotal / trials;
            string constraintNote = singleIntervalConstraint
                ? $" / 制約[{minQ},{maxQ}]遵守率: {(double)satisfiedCount / trials * 100.0:0.0}%"
                : string.Empty;

            return $"[100回ランタイム検証] 1玉実測平均={avg:0.00}（理論={report.TotalExpectedQuantityPerBob:0.00}）, 最小={minTotal}, 最大={maxTotal}{constraintNote}";
        }

        private static void DrawWaveProgressionGraph(Rect rect, WaveDifficultyReport[] reports, float maxY)
        {
            EditorGUI.DrawRect(rect, GRAPH_BG);
            DrawGrid(rect, 4);

            int n = reports.Length;
            if (n == 0)
            {
                return;
            }

            float padL = 32f;
            float padR = 14f;
            float padT = 14f;
            float padB = 20f;
            float w = Mathf.Max(1f, rect.width - padL - padR);
            float h = Mathf.Max(1f, rect.height - padT - padB);

            Vector3[] avgPts = new Vector3[n];
            Vector3[] l2Pts = new Vector3[n];
            Vector3[] scorePts = new Vector3[n];

            for (int i = 0; i < n; i++)
            {
                float x = rect.x + padL + (n == 1 ? w * 0.5f : w * i / (n - 1));
                avgPts[i] = new Vector3(x, rect.y + padT + h * (1f - Mathf.Clamp01((float)reports[i].TrapezoidalAverageDensity / maxY)), 0f);
                l2Pts[i] = new Vector3(x, rect.y + padT + h * (1f - Mathf.Clamp01((float)reports[i].L2RmsDensity / maxY)), 0f);
                scorePts[i] = new Vector3(x, rect.y + padT + h * (1f - Mathf.Clamp01((float)reports[i].BlendedDifficultyScore / maxY)), 0f);

                GUI.Label(new Rect(x - 12f, rect.yMax - 17f, 28f, 16f), $"W{i + 1}", EditorStyles.miniLabel);
            }

            Handles.BeginGUI();
            if (n >= 2)
            {
                Handles.color = AVG_LINE_COLOR;
                Handles.DrawAAPolyLine(2.5f, avgPts);
                Handles.color = L2_LINE_COLOR;
                Handles.DrawAAPolyLine(2.5f, l2Pts);
                Handles.color = SCORE_LINE_COLOR;
                Handles.DrawAAPolyLine(2.5f, scorePts);
            }

            Handles.EndGUI();

            GUI.Label(new Rect(rect.x + 4f, rect.y + 2f, 40f, 14f), maxY.ToString("0.0"), EditorStyles.miniLabel);
            GUI.Label(new Rect(rect.x + 4f, rect.yMax - 30f, 40f, 14f), "0.0", EditorStyles.miniLabel);
        }

        private static void DrawIntervalCurveGraph(Rect rect, WaveDifficultyReport report)
        {
            EditorGUI.DrawRect(rect, GRAPH_BG);
            DrawGrid(rect, 3);

            float padL = 32f;
            float padR = 12f;
            float padT = 12f;
            float padB = 18f;
            float w = Mathf.Max(1f, rect.width - padL - padR);
            float h = Mathf.Max(1f, rect.height - padT - padB);

            float maxY = Mathf.Max(0.5f, (float)report.PeakDensity * 1.2f);
            float spanT = Mathf.Max(0.001f, report.EndTime - report.StartTime);

            // 各区間の帯と分割点ラインを描画
            for (int i = 0; i < report.Intervals.Count; i++)
            {
                IntervalDpResult iv = report.Intervals[i];
                float x0 = rect.x + padL + w * ((iv.StartTime - report.StartTime) / spanT);
                float x1 = rect.x + padL + w * ((iv.EndTime - report.StartTime) / spanT);
                EditorGUI.DrawRect(new Rect(x0, rect.y + padT, Mathf.Max(1f, x1 - x0), h), i % 2 == 0 ? INTERVAL_BAND_A : INTERVAL_BAND_B);
                GUI.Label(new Rect(x0 + 2f, rect.yMax - 16f, Mathf.Max(30f, x1 - x0), 15f), iv.Label, EditorStyles.miniLabel);
            }

            // 平均密度の基準線
            float avgY = rect.y + padT + h * (1f - Mathf.Clamp01((float)report.TrapezoidalAverageDensity / maxY));
            EditorGUI.DrawRect(new Rect(rect.x + padL, avgY, w, 1f), AVG_LINE_COLOR);

            // 区分線形曲線 f(x)
            IReadOnlyList<Vector2> curve = report.CurvePoints;
            if (curve.Count >= 2)
            {
                Vector3[] pts = new Vector3[curve.Count];
                for (int i = 0; i < curve.Count; i++)
                {
                    float x = rect.x + padL + w * ((curve[i].x - report.StartTime) / spanT);
                    float y = rect.y + padT + h * (1f - Mathf.Clamp01(curve[i].y / maxY));
                    pts[i] = new Vector3(x, y, 0f);
                }

                Handles.BeginGUI();
                Handles.color = L2_LINE_COLOR;
                Handles.DrawAAPolyLine(3f, pts);
                Handles.EndGUI();
            }

            GUI.Label(new Rect(rect.x + 4f, rect.y + 2f, 40f, 14f), maxY.ToString("0.0"), EditorStyles.miniLabel);
        }

        private static void DrawProbabilityHistogram(Rect rect, IntervalDpResult iv)
        {
            EditorGUI.DrawRect(rect, GRAPH_BG);
            IReadOnlyList<double> probs = iv.TerminalQuantityProbabilities;
            if (probs == null || probs.Count == 0)
            {
                return;
            }

            // 表示範囲：確率が 0.001 以上の最大 q まで（最低 8 列）
            int maxDisplayQ = Math.Min(probs.Count - 1, Math.Max(8, iv.UseQuantityConstraint ? iv.MaxQuantity + 2 : 8));
            double maxProb = 0.05;
            for (int q = 0; q < probs.Count; q++)
            {
                if (probs[q] > maxProb) maxProb = probs[q];
                if (probs[q] >= 0.001 && q > maxDisplayQ) maxDisplayQ = q;
            }

            int bars = maxDisplayQ + 1;
            float padL = 6f;
            float padR = 6f;
            float padT = 8f;
            float padB = 16f;
            float w = Mathf.Max(1f, rect.width - padL - padR);
            float h = Mathf.Max(1f, rect.height - padT - padB);
            float barW = w / Math.Max(1, bars);

            for (int q = 0; q < bars; q++)
            {
                double p = q < probs.Count ? probs[q] : 0.0;
                float barH = (float)(p / maxProb) * h;
                float x = rect.x + padL + q * barW;
                bool inRange = !iv.UseQuantityConstraint || (q >= iv.MinQuantity && q <= iv.MaxQuantity);
                Color col = inRange ? IN_RANGE_BAR_COLOR : OUT_RANGE_BAR_COLOR;
                EditorGUI.DrawRect(new Rect(x + 1f, rect.y + padT + (h - barH), Mathf.Max(1f, barW - 2f), barH), col);

                if (bars <= 18 || q % 2 == 0)
                {
                    GUI.Label(new Rect(x, rect.yMax - 15f, Mathf.Max(18f, barW), 14f), q.ToString(), EditorStyles.miniLabel);
                }
            }
        }

        private static void DrawGrid(Rect rect, int horizontalLines)
        {
            for (int i = 1; i <= horizontalLines; i++)
            {
                float y = rect.y + rect.height * i / (horizontalLines + 1);
                EditorGUI.DrawRect(new Rect(rect.x, y, rect.width, 1f), GRID_COLOR);
            }
        }

        private static void DrawLegendChip(string label, Color color)
        {
            Rect r = GUILayoutUtility.GetRect(12f, 14f, GUILayout.Width(12f));
            r.y += 2f;
            r.height = 10f;
            EditorGUI.DrawRect(r, color);
            GUILayout.Label(label, EditorStyles.miniLabel);
            GUILayout.Space(8f);
        }
    }
}
