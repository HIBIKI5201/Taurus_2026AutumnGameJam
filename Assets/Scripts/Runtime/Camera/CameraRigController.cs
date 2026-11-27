using SymphonyFrameWork.System.ServiceLocate;
using Taurus.Runtime.Game;
using Unity.Cinemachine;
using UnityEngine;

namespace Taurus.Runtime.Camera
{
    /// <summary>
    /// Cinemachine 3（CinemachineCamera）の優先度を切り替えて、
    /// ゲーム状態（Title / Rules / Playing(1本・2本) / Result）に応じたカメラワークを制御する。
    /// GameScene の CameraRig オブジェクトにアタッチし、ServiceLocator 経由で GameController と連携する。
    ///
    /// 【引き継ぎ・調整ポイント】
    /// - 各カメラの構図や画角を変えたい場合は、GameScene 内の CM_〇〇 オブジェクトの Transform と Lens.FieldOfView を直接編集する。
    /// - ブレンド時間やカーブを変えたい場合は、Main Camera の CinemachineBrain.DefaultBlend を編集する。
    /// </summary>
    public class CameraRigController : MonoBehaviour
    {
        private const int ACTIVE_PRIORITY = 20;
        private const int INACTIVE_PRIORITY = 0;

        [SerializeField] private GameController _gameController;

        [Header("Cinemachine Cameras（状態別カメラ）")]
        [SerializeField] private CinemachineCamera _titleCamera;
        [SerializeField] private CinemachineCamera _rulesCamera;
        [SerializeField] private CinemachineCamera _gameSingleCamera;
        [SerializeField] private CinemachineCamera _gameDualCamera;
        [SerializeField] private CinemachineCamera _resultCamera;

        private GameController _subscribedController;

        private void OnEnable()
        {
            ServiceLocator.RegisterInstance(this, LocateTypeEnum.Locator);
            ResolveAndBindGameController();
        }

        private void OnDisable()
        {
            UnbindGameController();
            ServiceLocator.UnregisterInstance(this);
        }

        /// <summary> エディタ構築ツール（ProjectSceneSetupTool）からカメラ参照を一括設定する。 </summary>
        public void ConfigureCameras(
            GameController gameController,
            CinemachineCamera titleCamera,
            CinemachineCamera rulesCamera,
            CinemachineCamera gameSingleCamera,
            CinemachineCamera gameDualCamera,
            CinemachineCamera resultCamera)
        {
            _gameController = gameController;
            _titleCamera = titleCamera;
            _rulesCamera = rulesCamera;
            _gameSingleCamera = gameSingleCamera;
            _gameDualCamera = gameDualCamera;
            _resultCamera = resultCamera;
        }

        private void ResolveAndBindGameController()
        {
            if (ServiceLocator.TryGetInstance<GameController>(out GameController located))
            {
                BindGameController(located);
                return;
            }

            if (_gameController != null)
            {
                BindGameController(_gameController);
            }
            else
            {
                ApplyCameraForCurrentState();
            }

            ServiceLocator.RegisterAfterLocate<GameController>(HandleGameControllerLocated);
        }

        private void HandleGameControllerLocated(GameController controller)
        {
            if (this == null || !isActiveAndEnabled)
            {
                return;
            }

            BindGameController(controller);
        }

        private void BindGameController(GameController controller)
        {
            if (controller == null || ReferenceEquals(_subscribedController, controller))
            {
                return;
            }

            UnbindGameController();
            _subscribedController = controller;
            _gameController = controller;

            _subscribedController.OnPhaseChanged += HandlePhaseChanged;
            _subscribedController.OnWaveStarted += HandleWaveStarted;

            // 有効化・接続時点の状態に合わせて初期カメラを反映
            ApplyCameraForCurrentState();
        }

        private void UnbindGameController()
        {
            if (_subscribedController == null)
            {
                return;
            }

            _subscribedController.OnPhaseChanged -= HandlePhaseChanged;
            _subscribedController.OnWaveStarted -= HandleWaveStarted;
            _subscribedController = null;
        }

        private void HandlePhaseChanged(GamePhase phase)
        {
            ApplyCameraForCurrentState();
        }

        private void HandleWaveStarted(int wave)
        {
            // Wave 1-2(1本) → Wave 3-4(2本) のように本数が切り替わるタイミングでカメラ距離を切り替える
            ApplyCameraForCurrentState();
        }

        private void ApplyCameraForCurrentState()
        {
            GameController controller = _subscribedController != null ? _subscribedController : _gameController;
            if (controller == null)
            {
                ActivateCamera(_titleCamera);
                return;
            }

            switch (controller.CurrentPhase)
            {
                case GamePhase.Title:
                    ActivateCamera(_titleCamera);
                    break;
                case GamePhase.Rules:
                    ActivateCamera(_rulesCamera);
                    break;
                case GamePhase.Playing:
                case GamePhase.Success:
                // リザルト画面では失敗したWaveをリプレイするので、プレイ中と同じ画角で振り子を画面中央に映す
                case GamePhase.Result:
                    bool isDual = controller.ActivePendulums != null && controller.ActivePendulums.Count >= 2;
                    ActivateCamera(isDual ? _gameDualCamera : _gameSingleCamera);
                    break;
            }
        }

        private void ActivateCamera(CinemachineCamera target)
        {
            SetPriority(_titleCamera, target == _titleCamera ? ACTIVE_PRIORITY : INACTIVE_PRIORITY);
            SetPriority(_rulesCamera, target == _rulesCamera ? ACTIVE_PRIORITY : INACTIVE_PRIORITY);
            SetPriority(_gameSingleCamera, target == _gameSingleCamera ? ACTIVE_PRIORITY : INACTIVE_PRIORITY);
            SetPriority(_gameDualCamera, target == _gameDualCamera ? ACTIVE_PRIORITY : INACTIVE_PRIORITY);
            SetPriority(_resultCamera, target == _resultCamera ? ACTIVE_PRIORITY : INACTIVE_PRIORITY);
        }

        private static void SetPriority(CinemachineCamera cam, int priorityValue)
        {
            if (cam == null)
            {
                return;
            }

            cam.Priority = new PrioritySettings
            {
                Enabled = true,
                Value = priorityValue,
            };
        }
    }
}
