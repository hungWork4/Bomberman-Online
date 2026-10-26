# Hướng Dẫn Đồng Bộ Hóa Di Chuyển & Kỹ Năng Nhân Vật (Movement & Action Synchronization)

Tài liệu này đi sâu vào chi tiết cách thức hoạt động của hệ thống đồng bộ hóa di chuyển, các cơ chế chống lag (lag compensation), phương pháp tối ưu hóa băng thông mạng và cách tạo ra trải nghiệm phản hồi tức thì (immediate responsiveness) cho người chơi trong dự án Boss Room.

---

## 1. Tổng Quan Mô Hình Đồng Bộ Hóa (Synchronization Core Model)

Dự án sử dụng mô hình **Server-Authoritative (Server làm trọng tài)** thông qua thư viện **Unity Netcode for GameObjects (NGO)**. 

* **Vật lý & Vị trí gốc thuộc quyền Server:**
  Server quản lý vị trí thực tế của nhân vật thông qua [ServerCharacterMovement.cs](file:///d:/Project/Personal/BOSSROOMSAMPLE/Assets/Scripts/Gameplay/GameplayObjects/Character/ServerCharacterMovement.cs). Mọi va chạm vật lý, tính toán tìm đường tránh chướng ngại vật bằng `NavMeshAgent` đều chạy trên Server trong vòng lặp `FixedUpdate()`.
* **Tách biệt hiển thị đồ họa (Visual Separation):**
  Lớp hiển thị [ClientCharacter.cs](file:///d:/Project/Personal/BOSSROOMSAMPLE/Assets/Scripts/Gameplay/GameplayObjects/Character/ClientCharacter.cs) (chứa Animator và Renderers) được đặt làm con của GameObject root (nơi chứa `ServerCharacter` điều khiển vật lý). Sự phân tách này cho phép Client dịch chuyển đồ họa hiển thị một cách mượt mà và chạy các hoạt ảnh thử nghiệm độc lập mà không ảnh hưởng trực tiếp đến trạng thái vật lý thực trên Server.

---

## 2. Cách Tạo Cảm Giác Phản Hồi Tức Thì (Immediate Responsiveness)

Để người chơi không cảm thấy bị khựng hay trễ (input lag) khi chơi qua mạng có độ trễ lớn (RTT > 200ms), game áp dụng cơ chế **Anticipation (Dự báo trước)** cho cả di chuyển và kỹ năng:

### 2.1. Dự báo di chuyển (Client-Side Movement Anticipation)
1. **Bắt Input tức thời:** Khi người chơi click chuột di chuyển, [ClientInputSender.cs](file:///d:/Project/Personal/BOSSROOMSAMPLE/Assets/Scripts/Gameplay/UserInput/ClientInputSender.cs) sẽ kiểm tra nhanh xem tọa độ đó có gần NavMesh không, sau đó kích hoạt sự kiện cục bộ `ClientMoveEvent` ngay trong cùng frame hiển thị đó.
2. **Chạy hoạt ảnh tức thì:** [ClientCharacter.cs](file:///d:/Project/Personal/BOSSROOMSAMPLE/Assets/Scripts/Gameplay/GameplayObjects/Character/ClientCharacter.cs) lắng nghe sự kiện này và gọi trigger đổi trạng thái hoạt ảnh ngay lập tức:
   ```csharp
   OurAnimator.SetTrigger(m_VisualizationConfiguration.AnticipateMoveTriggerID);
   ```
3. **Kết quả:** Nhân vật nhấc chân chạy ngay lập tức trên màn hình của người chơi trước khi gói tin mạng gửi đến Server và phản hồi ngược lại.

### 2.2. Dự báo kỹ năng (Client-Side Action Anticipation)
Được triển khai trong [ClientActionPlayer.cs](file:///d:/Project/Personal/BOSSROOMSAMPLE/Assets/Scripts/Gameplay/Action/ActionPlayers/ClientActionPlayer.cs), đây là kỹ thuật tạo ảo giác phản hồi tức thì cho việc ra chiêu:
1. **Khởi chạy thử nghiệm (Opportunistic Playback):** Khi người chơi nhấn phím kỹ năng, hệ thống gọi `AnticipateAction`. Client sẽ tự tạo một phiên bản hành động cục bộ (`ActionFX`) và chạy trước hiệu ứng hình ảnh (vd: vung kiếm, chuẩn bị bắn đạn).
2. **Khớp lệnh xác nhận từ Server:** 
   * Khi Server duyệt yêu cầu và phát tín hiệu RPC `ClientPlayActionRpc` xuống các Client để chạy kỹ năng chính thức.
   * Client kiểm tra danh sách hành động đang chơi thử nghiệm (`m_PlayingActions`). Nếu tìm thấy một hành động trùng khớp (`ActionID`), nó sẽ chuyển đổi trạng thái thành chạy chính thức mà **không khởi động lại hoạt ảnh**. Người chơi trải nghiệm một luồng hoạt ảnh mượt mà liên tục.
3. **Hệ thống bảo vệ (Safeguard Timeout):** Nếu sau `k_AnticipationTimeoutSeconds = 1f` (1 giây) mà Client vẫn không nhận được xác nhận từ Server (vd: do Server từ chối vì nhân vật bị choáng, hết năng lượng, hoặc mất kết nối mạng), hành động thử nghiệm sẽ bị hủy (`CancelClient()`) để khôi phục trạng thái đúng đắn.

---

## 3. Hệ Thống Chống Lag & Làm Mượt Hoạt Ảnh (Lag Compensation & Smoothing)

Để tránh hiện tượng giật cục, giật hình khi truyền tọa độ qua Internet, Boss Room áp dụng hai cơ chế làm mượt:

### 3.1. Nội suy tọa độ mạng (Network Interpolation)
Vị trí thực tế của root GameObject được đồng bộ từ Server xuống các Client thông qua component `NetworkTransform`.
* **Cơ chế:** Client không dịch chuyển tức thời nhân vật tới tọa độ mới nhận được từ Server. `NetworkTransform` sẽ lưu trữ một hàng đợi các tọa độ trong quá khứ và thực hiện **nội suy (interpolation)** giữa các điểm đó theo thời gian thực để hiển thị nhân vật di chuyển liên tục, không bị giật lag ngay cả khi gói tin mạng bị suy hao hoặc trễ nhẹ.

### 3.2. Khử giật hình camera trên Host (Host Jitter Mitigation)
Trên máy Host (vừa là Server vừa là Client):
* **Vấn đề:** Chuyển động vật lý của nhân vật diễn ra trong vòng lặp `FixedUpdate()` (thường là 50Hz hoặc 60Hz cố định). Tuy nhiên, Camera di chuyển theo nhân vật lại chạy trong vòng lặp `Update()` (tốc độ khung hình không giới hạn, vd: 144Hz). Sự lệch pha này gây ra hiện tượng giật hình camera cực kỳ rõ ràng (Camera Jitter).
* **Giải pháp:** [ClientCharacter.cs](file:///d:/Project/Personal/BOSSROOMSAMPLE/Assets/Scripts/Gameplay/GameplayObjects/Character/ClientCharacter.cs) sử dụng cấu trúc [PositionLerper.cs](file:///d:/Project/Personal/BOSSROOMSAMPLE/Assets/Scripts/Utils/PositionLerper.cs) và [RotationLerper.cs](file:///d:/Project/Personal/BOSSROOMSAMPLE/Assets/Scripts/Utils/RotationLerper.cs) để liên tục nội suy vị trí phần đồ họa hiển thị hướng về vị trí vật lý thực tế trong `Update()`:
  ```csharp
  if (IsHost)
  {
      m_LerpedPosition = m_PositionLerper.LerpPosition(m_LerpedPosition, serverCharacter.physicsWrapper.Transform.position);
      m_LerpedRotation = m_RotationLerper.LerpRotation(m_LerpedRotation, serverCharacter.physicsWrapper.Transform.rotation);
      transform.SetPositionAndRotation(m_LerpedPosition, m_LerpedRotation);
  }
  ```
  *(Các máy Client thông thường không cần chạy đoạn mã này vì `NetworkTransform` đã tự động xử lý nội suy mịn trên root).*

### 3.3. Đồng bộ trạng thái gián tiếp (Indirect State Sync)
Thay vì liên tục truyền dữ liệu số thực chi tiết về tốc độ di chuyển hay gia tốc qua mạng (rất tốn băng thông và dễ bị lệch hoạt ảnh), Server sử dụng một `NetworkVariable<MovementStatus>` để đồng bộ trạng thái tổng quát (ví dụ: `Normal`, `Slowed`, `Hasted`, `Walking`, `Idle`).
* Khi biến này thay đổi, hàm `OnMovementStatusChanged` trên Client được gọi và gán biến vận tốc tương ứng vào `Animator` của Client:
  ```csharp
  OurAnimator.SetFloat(m_VisualizationConfiguration.SpeedVariableID, m_CurrentSpeed);
  ```
* Hệ thống hoạt ảnh trên mỗi máy Client sẽ tự tính toán để nhân vật chuyển động tương thích với trạng thái đó mà không cần đồng bộ từng frame hoạt ảnh qua mạng.

---

## 4. Các Biện Pháp Tối Ưu Hóa Băng Thông (Bandwidth Optimizations)

Để giảm thiểu tối đa dung lượng dữ liệu truyền tải trên đường truyền mạng (đặc biệt khi có nhiều người chơi cùng lúc):

### 4.1. Giới hạn tần suất gửi di chuyển (Input Rate Limiting)
* Trong [ClientInputSender.cs](file:///d:/Project/Personal/BOSSROOMSAMPLE/Assets/Scripts/Gameplay/UserInput/ClientInputSender.cs), biến `m_LastSentMove` quản lý thời gian gửi gói tin di chuyển gần nhất.
* Người chơi chỉ có thể gửi gói tin di chuyển mới cách gói tin cũ tối thiểu **40ms** (tương đương tối đa **25 gói tin/giây**) qua hằng số `k_MoveSendRateSeconds = 0.04f`.
* **Lợi ích:** Ngăn chặn việc Client liên tục gửi hàng trăm RPC di chuyển mỗi giây khi người chơi rê hoặc nhấp chuột điên cuồng, giảm tải lớn cho băng thông upstream của người chơi và CPU xử lý của Server.

### 4.2. Chỉ truyền dữ liệu tối giản (Minimal RPC Payload)
* RPC di chuyển `ServerSendCharacterInputRpc(Vector3 movementTarget)` chỉ truyền tải duy nhất một tọa độ đích (`Vector3`).
* Server sẽ tự tính toán đường đi chi tiết (pathfinding) từ vị trí hiện tại đến điểm đích trên bản đồ của Server. Client không cần gửi toàn bộ danh sách các nút tọa độ trung gian dọc đường đi, giúp tiết kiệm lượng lớn dữ liệu truyền tải.

### 4.3. Tối ưu hóa GC Alloc tránh nghẽn luồng chính
* Game sử dụng `Physics.RaycastNonAlloc` trong vòng lặp cập nhật Input để tránh tạo ra rác bộ nhớ (Garbage Collection Allocations) mỗi lần người chơi kiểm tra điểm nhấp chuột.
* Việc không cấp phát bộ nhớ động liên tục giúp CPU tránh được các pha dọn rác (GC spikes), giữ cho FPS ổn định để hoạt động đồng bộ hóa trên luồng chính diễn ra đều đặn, không bị trễ khung hình.
