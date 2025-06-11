using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Gpm.Ui;
using Mingle.Dev.JHC_TEST._02._Scripts._01._AgitListView;
using Mingle.Dev.KSK_Test._02.Scripts.Utility;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Mingle.Dev.KSK_Test._02.Scripts.Muster
{
    public class MusterPanelSystem : MonoBehaviour
    {
        [SerializeField] private InfiniteScroll infiniteScroll;
        [SerializeField] private TMP_InputField searchInputField;
        [SerializeField] private TextMeshProUGUI guideText;
        [SerializeField] private TextMeshProUGUI emptyStateText;

        private Dictionary<string, bool> _cachedPresenceStatus = new Dictionary<string, bool>();
        public Dictionary<string, bool> GetCachedPresenceStatus() => _cachedPresenceStatus;

        // 친구 정보 담기
        private List<MusterProfileItemData> friendList = new List<MusterProfileItemData>();
        // 원본 친구 리스트 (검색을 위한 전체 목록 저장)
        private List<MusterProfileItemData> _originalFriendList = new List<MusterProfileItemData>();
        
        // 검색 지연 처리 변수
        private readonly float _searchDelay = 0.5f;
        private float _searchTimer = 0f;
        private string _cachingText = "";
        private bool _isSearching = false;
        private bool _isSearchMode = false;
        
        private VerticalDraggableRect _verticalDraggableRect;

        private void Awake()
        {
            // 검색 입력 필드 이벤트 등록
            if (searchInputField != null)
            {
                searchInputField.onValueChanged.AddListener(SearchFriend);
            }

            _verticalDraggableRect = GetComponent<VerticalDraggableRect>();
        }

        private void SetupFriendList(List<FriendListType> friends)
        {
            friendList.Clear();
            infiniteScroll.Clear();

            // 친구 정보 받아와서, friendList에 담기, UI 초기화
            for (int i = 0; i < friends.Count; i += 1)
            {
                var nickname = friends[i].nickname;
                var thumbnailUrl = friends[i].close_thumb;
                var userID = friends[i].user_id;
                var isOnline = _cachedPresenceStatus.TryGetValue(friends[i].user_id, out bool status) && status;

                var data = new MusterProfileItemData { 
                    UserID = userID, 
                    ThumbnailUrl = thumbnailUrl, 
                    Nickname = nickname, 
                    IsOnline = isOnline 
                };

                // 친구로 추가
                friendList.Add(data);
                infiniteScroll.InsertData(data);
            }

            // 원본 친구 리스트 저장 (검색용)
            _originalFriendList = new List<MusterProfileItemData>(friendList);
    
            // 친구 수에 따라 가이드 텍스트와 emptyStateText 활성화/비활성화
            bool hasFriends = friends.Count > 0;
            if (guideText != null)
            {
                guideText.gameObject.SetActive(hasFriends);
            }
    
            // 친구가 없을 때 emptyStateText 활성화 및 메시지 설정
            if (emptyStateText != null)
            {
                emptyStateText.gameObject.SetActive(!hasFriends);
                if (!hasFriends)
                {
                    emptyStateText.text = LocalizationManager.GetLocalizedText(
                        "EmoTable",
                        "UI_Emo_ComeHere_NoFriends",
                        "소집할 수 있는 친구가 없어요"
                    );
                }
            }
        }

        private async void OnEnable()
        {
            // ShowMessage();
            infiniteScroll.Clear();
            friendList.Clear();
            _originalFriendList.Clear();
            _isSearchMode = false;
            
            // 검색 필드 초기화
            if (searchInputField != null)
            {
                searchInputField.text = "";
            }
            
            // 패널이 활성화될 때는 일단 가이드 텍스트 비활성화
            if (guideText != null)
            {
                guideText.gameObject.SetActive(false);
            }
            
            if (emptyStateText != null)
            {
                emptyStateText.gameObject.SetActive(false);
            }
            
            _verticalDraggableRect.MoveToMaxPosition();
            
            await LoadFriendList();
        }

        public void OpenPanel()
        {
            gameObject.SetActive(true);
        }

        private async UniTask LoadFriendList()
        {
            // 캐시 초기화
            _cachedPresenceStatus.Clear();

            string friendListJson = await APIManager.GetFriendListAsync(APIManager.SpecifyToken(), "me");
            if (!string.IsNullOrEmpty(friendListJson))
            {
                List<FriendListType> friendList = JsonConvert.DeserializeObject<List<FriendListType>>(friendListJson);

                try
                {
                    if (PNManager.instance != null && !string.IsNullOrEmpty(Information.UserId))
                    {
                        PNManager.instance.userId = Information.UserId;

                        // 모든 친구들의 온라인 상태를 한 번에 가져오기
                        _cachedPresenceStatus = await PNManager.instance.GetUserPresenceAsync(
                            friendList.Select(f => f.user_id).ToList());

                        var sortedFriends = friendList
                            .OrderByDescending(f => _cachedPresenceStatus.GetValueOrDefault(f.user_id, false))
                            .ThenBy(f => GetSortPriority(f.nickname))
                            .ThenBy(f => f.nickname)
                            .ToList();

                        SetupFriendList(sortedFriends);
                    }
                    else
                    {
                        // 온라인 상태 없이 이름순으로만 정렬
                        var nameOnlySortedFriends = friendList
                            .OrderBy(f => GetSortPriority(f.nickname))
                            .ThenBy(f => f.nickname)
                            .ToList();

                        SetupFriendList(nameOnlySortedFriends);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"친구 목록 처리 중 오류 발생: {e.Message}");
                    SetupFriendList(friendList);
                }
            }
            else
            {
                // API 응답이 없거나 비어있을 경우 빈 리스트 설정 (가이드 텍스트 비활성화)
                SetupFriendList(new List<FriendListType>());
            }
        }

        // 친구 리스트 정렬 규칙
        private int GetSortPriority(string nickname)
        {
            if (string.IsNullOrEmpty(nickname)) return 999;

            char firstChar = nickname[0];

            if (firstChar is >= '가' and <= '힣') return 0;
            if (firstChar is >= 'A' and <= 'Z') return 1;
            if (firstChar is >= 'a' and <= 'z') return 2;
            if (firstChar is >= '0' and <= '9') return 3;

            // 특수문자
            return 4;
        }

        // private void ShowMessage()
        // {
        //     Toast.Show("<color=black>소집을 원하는 친구를 터치하여 불러보세요!</color>",
        //         3f, ToastColor.White, ToastPosition.TopCenter);
        // }
        
        // 친구 검색 함수
        public void SearchFriend(string searchText)
        {
            // 검색어가 없을 때 전체 친구 목록 표시
            if (string.IsNullOrEmpty(searchText))
            {
                _cachingText = "";
                _isSearching = false;
                _isSearchMode = false;
                
                // 전체 친구 목록 다시 표시
                infiniteScroll.Clear();
                foreach (var friend in _originalFriendList)
                {
                    infiniteScroll.InsertData(friend);
                }
                
                // 전체 친구 수에 따라 가이드 텍스트와 emptyStateText 표시 여부 결정
                if (guideText != null)
                {
                    guideText.gameObject.SetActive(_originalFriendList.Count > 0);
                }
                
                if (emptyStateText != null)
                {
                    bool hasFriends = _originalFriendList.Count > 0;
                    emptyStateText.gameObject.SetActive(!hasFriends);
                    emptyStateText.text = LocalizationManager.GetLocalizedText(
                        "EmoTable",
                        "UI_Emo_ComeHere_NoFriends",
                        "소집할 수 있는 친구가 없어요"
                    );
                }
                
                return;
            }

            var koreanText = searchText;
            _cachingText = koreanText;
            _isSearching = true;
            _isSearchMode = true;
            
            // 검색 딜레이 적용 (타이핑 중에는 검색하지 않음)
            if (_searchTimer < _searchDelay) return;
            
            // 검색 시작
            _searchTimer = 0f;
            _cachingText = "";
            
            FilterFriendList(koreanText);
        }

        // 친구 목록 필터링 함수
        private void FilterFriendList(string keyword)
        {
            if (string.IsNullOrEmpty(keyword))
            {
                return;
            }
            
            // 검색어로 친구 필터링
            var filteredFriends = _originalFriendList
                .Where(f => f.Nickname.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderByDescending(f => f.IsOnline)
                .ThenBy(f => GetSortPriority(f.Nickname))
                .ThenBy(f => f.Nickname)
                .ToList();
            
            // UI 업데이트
            infiniteScroll.Clear();
            foreach (var friend in filteredFriends)
            {
                infiniteScroll.InsertData(friend);
            }
            
            // 필터링된 친구 수에 따라 가이드 텍스트와 emptyStateText 표시 여부 결정
            bool hasResults = filteredFriends.Count > 0;
            
            if (guideText != null)
            {
                guideText.gameObject.SetActive(hasResults);
            }
            
            if (emptyStateText != null)
            {
                emptyStateText.gameObject.SetActive(!hasResults);
                if (!hasResults)
                {
                    emptyStateText.text = LocalizationManager.GetLocalizedText(
                        "EmptyStatesTable",
                        "UI_EmptyState_NoSearchResults",
                        "검색 결과가 없어요"
                    );
                }
            }
        }
        
        private void Update()
        {
            // 검색 타이머 업데이트
            if (_isSearching && _searchTimer < _searchDelay)
            {
                _searchTimer += Time.deltaTime;
            }
            else if (_isSearching)
            {
                if (string.IsNullOrEmpty(_cachingText)) return;
                
                FilterFriendList(_cachingText);
                _cachingText = "";
            }
        }
        
        private void OnDisable()
        {
            // 패널이 비활성화될 때 emptyStateText 비활성화
            if (emptyStateText != null)
            {
                emptyStateText.gameObject.SetActive(false);
            }
        }
    }
}