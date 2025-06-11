# unity-muster-notification-system

Unity muster system with Firebase push notifications and PubNub real-time messaging for social gaming

## 🛠 Tech Stack

• Unity 2021.3+
• C#
• Firebase Cloud Messaging (FCM)
• PubNub Real-time Messaging
• UniTask

## ⭐ Key Features

• 실시간 친구 소집 시스템
• 온라인/오프라인 상태별 알림 방식
• Firebase 푸시 알림 (백그라운드)
• PubNub 실시간 메시징 (포그라운드)
• 친구 검색 및 정렬 시스템
• 무한 스크롤 최적화
• 다국어 지원 UI

## 🎮 How It Works

1. 친구 목록에서 소집할 친구 선택
2. 친구가 온라인이면 실시간 알림, 오프라인이면 푸시 알림
3. 받은 친구는 팝업으로 수락/거절 선택
4. 수락 시 자동으로 해당 위치로 순간이동

## 🎯 System Flow

1. **친구 상태 확인**: PubNub Presence로 온라인 상태 실시간 감지
2. **알림 분기**: 온라인(PubNub) vs 오프라인(FCM Push)
3. **실시간 통신**: JSON 메시지로 소집 요청/응답 처리
4. **타이머 관리**: 180초 자동 만료 시스템
5. **UI 최적화**: 무한 스크롤로 대용량 친구 목록 처리
