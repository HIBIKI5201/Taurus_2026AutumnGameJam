using System.Collections.Generic;
using System.Text;
using SymphonyFrameWork.System.ServiceLocate;
using Taurus.Runtime.Game;
using Taurus.Runtime.Input;
using Taurus.Runtime.Pendulum;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Taurus.Develop.Debug
{
    /// <summary>
    /// Fキーでゲーム進行を止めずに表示切替できる開発・QA用デバッグオーバーレイ。
    /// 各玉の「実測通過数 / 予定通過数」「角度（度）」「高さ h = -cos(θ)」「向き」「直近TOP通過（0.35秒）」
    /// および「Wave時刻」「32bit乱数シード」を表示し、8秒時点で実測と予定が食い違えば赤色で強調する。
    /// </summary>
    public class DebugOverlay : MonoBehaviour
    {
        // 仕様書第11章準拠：直近TOP通過マーカーの表示持続時間（秒）
        private const float TOP_FLASH_WINDOW_SECONDS = 0.35f;

        [SerializeField] private GameController _gameController;
        [SerializeField] private GameObject _overlayRoot;
        [SerializeField] private Text _debugText;

        private readonly StringBuilder _builder = new StringBuilder(512);
        private GameInputActions _inputActions;
        private bool _isVisible;

        private void Awake()
        {
            // 起動時は非表示にしておき、Fキーで開閉する
            SetVisible(false);
        }

        private void OnEnable()
        {
            _inputActions ??= new GameInputActions();
            _inputActions.Debug.ToggleOverlay.performed += HandleToggleOverlayPerformed;
            _inputActions.Debug.Enable();

            ResolveGameController();
        }

        private void OnDisable()
        {
            if (_inputActions == null)
            {
                return;
            }

            _inputActions.Debug.ToggleOverlay.performed -= HandleToggleOverlayPerformed;
            _inputActions.Debug.Disable();
        }

        private void OnDestroy()
        {
            _inputActions?.Dispose();
            _inputActions = null;
        }

        private void ResolveGameController()
        {
            if (ServiceLocator.TryGetInstance<GameController>(out GameController located))
            {
                _gameController = located;
                return;
            }

            ServiceLocator.RegisterAfterLocate<GameController>(HandleGameControllerLocated);
        }

        private void HandleGameControllerLocated(GameController controller)
        {
            if (this == null || !isActiveAndEnabled)
            {
                return;
            }

            _gameController = controller;
        }

        /// <summary> エディタ構築ツール（ProjectSceneSetupTool）から参照を設定する。 </summary>
        public void ConfigureReferences(GameController gameController, GameObject overlayRoot, Text debugText)
        {
            _gameController = gameController;
            _overlayRoot = overlayRoot;
            _debugText = debugText;
        }

        private void Update()
        {
            if (_isVisible)
            {
                RefreshText();
            }
        }

        private void HandleToggleOverlayPerformed(InputAction.CallbackContext context)
        {
            SetVisible(!_isVisible);
        }

        private void SetVisible(bool visible)
        {
            _isVisible = visible;
            if (_overlayRoot != null)
            {
                _overlayRoot.SetActive(_isVisible);
            }

            if (_isVisible)
            {
                RefreshText();
            }
        }

        private void RefreshText()
        {
            if (_debugText == null || _gameController == null)
            {
                return;
            }

            _builder.Clear();
            _builder.AppendLine("DEBUG / Fで閉じる");
            _builder.Append("PHASE: ").Append(_gameController.CurrentPhase)
                .Append("  WAVE: ").Append(_gameController.CurrentWave)
                .Append("  T: ").Append(_gameController.WaveElapsedSeconds.ToString("0.00")).Append("s")
                .AppendLine();
            _builder.Append("SEED: ").Append(_gameController.RunSeed)
                .Append(" (0x").Append(_gameController.RunSeed.ToString("X8")).Append(')')
                .Append("  EXPECTED: ").Append(_gameController.ExpectedCount)
                .Append("  PLAYER: ").Append(_gameController.PlayerCount)
                .AppendLine();

            // 8〜10秒の減衰区間では計数が止まっていることを明示する
            if (_gameController.WaveElapsedSeconds >= 8.0f && _gameController.CurrentPhase == GamePhase.Playing)
            {
                _builder.AppendLine("<color=#ffd166>[TAIL 8-10s] 計数停止中</color>");
            }

            IReadOnlyList<PendulumMotion> pendulums = _gameController.ActivePendulums;
            float now = Time.unscaledTime;

            for (int i = 0; i < pendulums.Count; i++)
            {
                PendulumMotion p = pendulums[i];
                if (p == null)
                {
                    continue;
                }

                int parentExpected = p.ParentSchedule != null ? p.ParentSchedule.TotalExpectedTopCount : 0;
                AppendBobLine(
                    $"P{i + 1} [SEG {p.CurrentSegmentIndex}]",
                    p.ParentObservedCount,
                    parentExpected,
                    p.ParentAngleRad,
                    p.ParentHeight,
                    now - p.ParentLastTopTime < TOP_FLASH_WINDOW_SECONDS,
                    _gameController.WaveElapsedSeconds >= 8.0f);

                if (p.IsDoublePendulum)
                {
                    int childExpected = p.ChildSchedule != null ? p.ChildSchedule.TotalExpectedTopCount : 0;
                    AppendBobLine(
                        $" P{i + 1}-C [SEG {p.CurrentSegmentIndex}]",
                        p.ChildObservedCount,
                        childExpected,
                        p.ChildWorldAngleRad,
                        p.ChildHeight,
                        now - p.ChildLastTopTime < TOP_FLASH_WINDOW_SECONDS,
                        _gameController.WaveElapsedSeconds >= 8.0f);
                }
            }

            _debugText.text = _builder.ToString();
        }

        private void AppendBobLine(
            string label,
            int observed,
            int expected,
            double angleRad,
            double height,
            bool isRecentTop,
            bool isTailPhase)
        {
            int degrees = Mathf.RoundToInt((float)(angleRad * Mathf.Rad2Deg));
            string dir = angleRad >= 0.0 ? "→" : "←";
            string topMark = isRecentTop ? " <color=#d5ff58>TOP!</color>" : string.Empty;

            // 8秒の計画区間終了後に実測値と予定値が一致しない場合は不具合のため赤色で強調する
            bool isMismatch = isTailPhase && observed != expected;

            if (isMismatch)
            {
                _builder.Append("<color=#ff595e>");
            }

            _builder.Append(label)
                .Append(' ')
                .Append(observed).Append('/').Append(expected)
                .Append("  ").Append(degrees).Append("°")
                .Append(" h=").Append(height.ToString("0.00"))
                .Append(' ').Append(dir)
                .Append(topMark);

            if (isMismatch)
            {
                _builder.Append(" [MISMATCH!]</color>");
            }

            _builder.AppendLine();
        }
    }
}
