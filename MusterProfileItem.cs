using Cysharp.Threading.Tasks;
using Gpm.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Mingle.Dev.KSK_Test._02.Scripts.Muster
{
    public class MusterProfileItemData : InfiniteScrollData
    {
        #region Properties
        public string UserID;
        public string ThumbnailUrl;
        public string Nickname;
        public bool IsOnline = false;
        #endregion
    }

    /// <summary>
    /// 소집 프로필 UI 아이템을 관리하는 컴포넌트
    /// 각 친구의 프로필 프리팹에 부착되어 동작
    /// 클릭 감지, UI 업데이트 등 단일 프로필에 대한 처리
    /// </summary>
    public class MusterProfileItem : InfiniteScrollItem
    {
        #region SerializeField
        [Header("UI Components")]
        [SerializeField] private CommonProfile profile;
        [SerializeField] private TextMeshProUGUI nickname;
        [SerializeField] private Image musterIcon; // 소집 아이콘
        #endregion

        #region Unity Lifecycle
        private void OnEnable()
        {
            if (MusterManager.Instance != null)
            {
                MusterManager.Instance.OnMusterTimerExpired += UpdateMusterIcon;
            }
        }

        private void OnDisable()
        {
            if (MusterManager.Instance != null)
            {
                MusterManager.Instance.OnMusterTimerExpired -= UpdateMusterIcon;
            }

            if (profile != null)
            {
                profile.ClearImage();
            }
        }

        private void OnDestroy()
        {
            if (MusterManager.Instance != null && nickname != null)
            {
                MusterManager.Instance.CancelMusterTimer(nickname.text);
            }
        }
        #endregion

        #region Muster Request Handling
        private void OnProfileClick()
        {
            if (!MusterManager.Instance.IsMustered(nickname.text))
            {
                HandleMusterRequest().Forget();
            }
        }

        private async UniTask HandleMusterRequest()
        {
            if (scrollData is not MusterProfileItemData panelItem) return;

            if (musterIcon != null)
            {
                musterIcon.gameObject.SetActive(true);

                try
                {
                    await MusterManager.Instance.SendMusterRequest(panelItem.Nickname, panelItem.UserID, panelItem.ThumbnailUrl);
                    MusterManager.Instance.StartMusterTimer(panelItem.Nickname);
                }
                catch (System.Exception e)
                {
                    MusterManager.Instance.StartMusterTimer(panelItem.Nickname);
                    Debug.LogError($"Failed to send muster request: {e.Message}");
                }
            }
        }
        #endregion

        #region UI Update
        public override void UpdateData(InfiniteScrollData scrollData)
        {
            base.UpdateData(scrollData);

            var itemData = (MusterProfileItemData)scrollData;

            // * 닉네임, 썸네일, 소집 여부에 따른 아이콘 표시, 온/오프라인 업데이트
            nickname.text = itemData.Nickname;
            profile.SetOffline(!itemData.IsOnline);

            musterIcon.gameObject.SetActive(MusterManager.Instance.IsMustered(itemData.Nickname));

            // 이전 이미지와 동일한지 확인 후 최적화
            string currentThumb = profile.GetCurrentThumbnailUrl();

            if (profile.UserID != itemData.UserID || currentThumb != itemData.ThumbnailUrl)
            {
                // 이전 이미지 명시적 해제 후 새 이미지 설정
                profile.ClearImage();
                profile.SetUser(itemData.UserID, itemData.ThumbnailUrl);
            }

            var button = profile.GetComponent<Button>();
            if (button)
            {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(OnProfileClick);
            }
        }

        private void UpdateMusterIcon(string nickname)
        {
            musterIcon.gameObject.SetActive(false);
        }
        #endregion
    }
}