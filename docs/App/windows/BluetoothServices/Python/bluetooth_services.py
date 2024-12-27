import asyncio
from bleak import BleakScanner, BleakClient
import struct
import sys
import multiprocessing
import signal
import os
import time

# Python bleak 使用的是 BLE（Bluetooth Low Energy）掃描方式

class BluetoothTester:
    ####################################################################################################### 一般性服務
    # 可用的服務: Generic Access Profile
    GENERIC_ACCESS_UUID = "00001800-0000-1000-8000-00805f9b34fb"
    # 特徵值
    DEVICE_NAME_UUID = "00002a00-0000-1000-8000-00805f9b34fb" # 屬性: 可讀
    APPEARANCE_UUID = "00002a01-0000-1000-8000-00805f9b34fb" # 屬性: 可讀

    # 可用的服務: Generic Attribute Profile
    GENERIC_ATTRIBUTE_PROFILE_UUID = "00001801-0000-1000-8000-00805f9b34fb"
    # 特徵值
    SERVICE_CHANGED_UUID = "00002a05-0000-1000-8000-00805f9b34fb"

    # 可用的服務: Device Information
    DEVICE_INFO_UUID = "0000180a-0000-1000-8000-00805f9b34fb"
    # 特徵值
    MANUFACTURER_NAME_UUID = "00002a29-0000-1000-8000-00805f9b34fb" # 製造商名稱; 屬性: 可讀
    MODEL_NUMBER_UUID = "00002a24-0000-1000-8000-00805f9b34fb" # 型號; 屬性: 可讀
    
    # 可用的服務: Battery Service
    BATTERY_SERVICE_UUID = "0000180f-0000-1000-8000-00805f9b34fb"
    # 特徵值
    BATTERY_SERVICE_LEVEL_UUID = "00002a19-0000-1000-8000-00805f9b34fb" # 電池電量; 屬性: 可讀, 可通知

    # 可用的服務: Current Time Service
    CURRENT_TIME_SERVICE_UUID = "00001805-0000-1000-8000-00805f9b34fb"
    # 特徵值
    CURRENT_TIME_CHAR_UUID = "00002a2b-0000-1000-8000-00805f9b34fb" # 當前時間; 屬性: 可讀, 可通知
    LOCAL_TIME_INFO_UUID = "00002a0f-0000-1000-8000-00805f9b34fb" # 本地時間 屬性: 可讀
        
    ####################################################################################################### Apple 特定服務
    # Apple 特定服務: Apple Continuity Service
    APPLE_CONTINUITY_SERVICE_UUID = "d0611e78-bbb4-4591-a5f8-487910ae4366"
    # 特徵值
    APPLE_CONTINUITY_SERVICE_CHARACTERISTIC_UUID = "8667556c-9a37-4c91-84ed-54ee27d90049" # 屬性: 可讀, 可通知

    # Apple 特定服務: Apple Nearby Interaction
    APPLE_NEARBY_INTERACTION_UUID = "9fa480e0-4967-4542-9390-d343dc5d04ae"
    # 特徵值
    APPLE_NEARBY_INTERACTION_CHARACTERISTIC_UUID = "af0badb1-5b99-43cd-917a-a77bc549e3cc" # 屬性: 可讀, 可通知

    # Apple 特定服務: Apple Notification Center Service
    ANCS_SERVICE_UUID = "7905f431-b5ce-4e99-a40f-4b1e122d00d0"
    # 特徵值
    ANCS_SERVICE_CONTROL_POINT_UUID =  "69d1d8f3-45e1-49a8-9821-9bbdfdaad9d9" # 屬性: 可寫
    ANCS_SERVICE_NOTIFICATION_SOURCE_UUID = "9fbf120d-6301-42d9-8c58-25e699a21dbd" # 屬性: 可通知
    ANCS_SERVICE_DATA_SOURCE_UUID = "22eac6e9-24d6-4bb5-be44-b36ace7c7bfb" # 屬性: 可通知
    
    # Apple 特定服務: Apple Media Control
    APPLE_MEDIA_SERVICE_UUID = "89d3502b-0f36-433a-8ef4-c502ad55f8dc"
    # 特徵值
    APPLE_MEDIA_SERVICE_REMOTE_COMMAND_UUID = "9b3c81d8-57b1-4a8a-b8df-0e56f7ca51c2" # 屬性: 可寫, 可通知
    APPLE_MEDIA_SERVICE_ENTITY_UPDATE_UUID = "2f7cabce-808d-411f-9a0c-bb92ba96c102" # 屬性: 可寫, 可通知
    APPLE_MEDIA_SERVICE_ENTITY_ATTRIBUTE_UUID = "c6b2f38c-23ab-46d8-a6ab-a3a870bbd5d7" # 屬性: 可讀, 可寫

    def __init__(self, address="18:7E:B9:6A:B8:5D"):
        self.address = address
        self.client = None
        self.services_ready = asyncio.Event()
        self.available_services = set()

    async def ensure_connection(self):
        """確保藍牙連接是活躍的"""
        try:
            if not self.client or not self.client.is_connected:
                print("重新建立連接...")
                if self.client:
                    try:
                        await self.client.disconnect()
                    except:
                        pass
                self.client = BleakClient(self.address, timeout=10.0)
                await self.client.connect()
                print("連接成功！")
            return True
        except Exception as e:
            print(f"連接失敗: {e}")
            return False

    async def connect_and_read_characteristic(self, char_uuid, description):
        """建立新連接並讀取特徵值"""
        client = None
        try:
            print(f"建立連接以讀取{description}...")
            client = BleakClient(self.address, timeout=10.0)
            await client.connect()
            await asyncio.sleep(1)  # 等待連接穩定
            
            data = await client.read_gatt_char(char_uuid)
            result = data.decode()
            return result
        except Exception as e:
            print(f"無法讀取{description}: {str(e)}")
            return None
        finally:
            if client:
                try:
                    await client.disconnect()
                except:
                    pass

    async def read_characteristic_with_retry(self, char_uuid, description, max_retries=2, decode=True):
        """嘗試讀取特徵值，如果失敗則重試"""
        for attempt in range(max_retries):
            try:
                if await self.ensure_connection():
                    await asyncio.sleep(0.5)  # 短暫延遲以確保連接穩定
                    data = await self.client.read_gatt_char(char_uuid)
                    if decode:
                        return data.decode()
                    else:
                        return data
            except Exception as e:
                if attempt < max_retries - 1:
                    print(f"讀取{description}失敗，正在重試...")
                    await asyncio.sleep(1)  # 重試前等待
                else:
                    print(f"無法讀取{description}: {str(e)}")
        return None

    async def test_generic_access(self):
        """測試 Generic Access Profile"""
        print("\n測試 Generic Access Profile:")
        try:
            device_name = await self.read_characteristic_with_retry(
                self.DEVICE_NAME_UUID, 
                "設備名稱"
            )
            if device_name:
                print(f"設備名稱: {device_name}")
            
            appearance_bytes = await self.read_characteristic_with_retry(
                self.APPEARANCE_UUID, 
                "外觀特徵值",
                decode=False  # 不要解碼二進制數據
            )
            if appearance_bytes:
                appearance_value = int.from_bytes(appearance_bytes, byteorder='little')
                print(f"外觀特徵值: {appearance_value}")
        except Exception as e:
            print(f"讀取 Generic Access 時發生錯誤: {e}")

    async def test_device_information(self):
        """測試 Device Information Service"""
        print("\n測試 Device Information Service:")
        try:
            if not await self.ensure_connection():
                print("未連接到設備")
                return

            service = self.client.services.get_service(self.DEVICE_INFO_UUID)
            if not service:
                print("設備不支持 Device Information Service")
                return

            available_characteristics = {
                char.uuid: char 
                for char in service.characteristics
            }

            print("\n可用的設備信息特徵值:")
            for uuid, char in available_characteristics.items():
                print(f"- {uuid}: {char.description}")
                properties = []
                if "read" in char.properties:
                    properties.append("可讀")
                if "write" in char.properties:
                    properties.append("可寫")
                if "write-without-response" in char.properties:
                    properties.append("無回應可寫")
                if properties:
                    print(f"  屬性: {', '.join(properties)}")

            print("\n讀取設備信息:")
            if self.MANUFACTURER_NAME_UUID in available_characteristics:
                manufacturer = await self.read_characteristic_with_retry(
                    self.MANUFACTURER_NAME_UUID,
                    "製造商信息"
                )
                if manufacturer:
                    print(f"製造商: {manufacturer[:26]}")  # 限制為26字符

            if self.MODEL_NUMBER_UUID in available_characteristics:
                model = await self.read_characteristic_with_retry(
                    self.MODEL_NUMBER_UUID,
                    "型號信息"
                )
                if model:
                    print(f"型號: {model[:26]}")  # 限制為26字符

        except Exception as e:
            print(f"測試設備信息服務時發生錯誤: {e}")

    async def test_battery_service(self):
        """測試 Battery Service"""
        print("\n測試 Battery Service:")
        try:
            if not await self.ensure_connection():
                print("未連接到設備")
                return

            service = self.client.services.get_service(self.BATTERY_SERVICE_UUID)
            if not service:
                print("設備不支持 Battery Service")
                return

            battery_data = await self.read_characteristic_with_retry(
                self.BATTERY_SERVICE_LEVEL_UUID,
                "電池信息"
            )
            if battery_data:
                battery_level = int(battery_data.encode()[0])
                print(f"電池電量: {battery_level}%")

        except Exception as e:
            print(f"測試電池服務時發生錯誤: {e}")

    async def test_current_time(self):
        """測試 Current Time Service"""
        print("\n測試 Current Time Service:")
        try:
            if not await self.ensure_connection():
                print("未連接到設備")
                return

            service = self.client.services.get_service(self.CURRENT_TIME_SERVICE_UUID)
            if not service:
                print("設備不支持 Current Time Service")
                return

            time_data = await self.read_characteristic_with_retry(
                self.CURRENT_TIME_CHAR_UUID,
                "當前時間"
            )
            if time_data:
                data = time_data.encode()
                # 解析時間數據
                year = int.from_bytes(data[0:2], byteorder='little')
                month = data[2]
                day = data[3]
                hours = data[4]
                minutes = data[5]
                seconds = data[6]
                
                print(f"當前時間: {year}-{month:02d}-{day:02d} {hours:02d}:{minutes:02d}:{seconds:02d}")
        except Exception as e:
            print(f"測試時間服務時發生錯誤: {e}")

    async def discover_services(self):
        """發現並顯示所有服務和特徵值"""
        print("\n發現的服務和特徵值:")
        for service in self.client.services:
            print(f"\n服務: {service.uuid}")
            print(f"  描述: {service.description}")
            print("  特徵值:")
            for char in service.characteristics:
                print(f"    - UUID: {char.uuid}")
                print(f"      描述: {char.description}")
                properties = []
                if "read" in char.properties:
                    properties.append("可讀")
                if "write" in char.properties:
                    properties.append("可寫")
                if "write-without-response" in char.properties:
                    properties.append("無回應可寫")
                if "notify" in char.properties:
                    properties.append("可通知")
                if properties:
                    print(f"      屬性: {', '.join(properties)}")

    async def service_changed_handler(self, sender, data):
        """處理服務變更通知"""
        print("收到服務變更通知")
        self.services_ready.set()

    async def wait_for_services(self, timeout=10.0):
        """等待服務變為可用"""
        try:
            # 訂閱 Service Changed 特徵值
            service = self.client.services.get_service(self.GENERIC_ATTRIBUTE_PROFILE_UUID)
            if service:
                char = service.get_characteristic(self.SERVICE_CHANGED_UUID)
                if char and "notify" in char.properties:
                    await self.client.start_notify(
                        self.SERVICE_CHANGED_UUID,
                        self.service_changed_handler
                    )
                    print("正在等待服務變為可用...")
                    try:
                        await asyncio.wait_for(self.services_ready.wait(), timeout)
                        print("服務已就緒")
                    except asyncio.TimeoutError:
                        print("等待服務超時，繼續執行（服務可能已經可用）")
                else:
                    print("Service Changed 特徵值不可用，繼續執行")
            else:
                print("Generic Attribute Service 不可用，繼續執行")
        except Exception as e:
            print(f"等待服務時發生錯誤: {e}")

    async def run_all_tests(self):
        """運行所有測試"""
        try:
            print(f"正在連接到設備 ({self.address})...")
            self.client = BleakClient(self.address, timeout=6.0)
            await self.client.connect()
            print("連接成功！")

            # 等待服務變為可用
            await self.wait_for_services()

            # 首先發現並顯示所有服務和特徵值
            await self.discover_services()

            # 獲取所有可用的服務
            self.available_services = {
                service.uuid: service.description
                for service in self.client.services
            }

            print("\n可用的服務:")
            for uuid, desc in self.available_services.items():
                print(f"- {uuid}: {desc}")

            # 運行各個服務的測試
            await self.test_generic_access()
            await self.test_device_information()
            await self.test_battery_service()
            await self.test_current_time()

            # 檢查 ANCS 服務
            if self.ANCS_SERVICE_UUID.lower() in self.available_services:
                print("\nANCS 服務可用")
            else:
                print("\nANCS 服務不可用")

        except Exception as e:
            print(f"運行測試時發生錯誤: {e}")
        finally:
            if self.client:
                # 停止所有通知
                try:
                    await self.client.stop_notify(self.SERVICE_CHANGED_UUID)
                except:
                    pass
                # 斷開連接
                try:
                    await self.client.disconnect()
                except:
                    pass

def run_bluetooth_tests():
    """在單獨的進程中運行藍牙測試"""
    tester = BluetoothTester()
    asyncio.run(tester.run_all_tests())

def main():
    # 設置超時時間（秒）
    TIMEOUT = 30  # 增加超時時間，因為要測試更多服務

    # 創建藍牙測試進程
    process = multiprocessing.Process(target=run_bluetooth_tests)
    
    try:
        print("開始執行藍牙服務測試...")
        process.start()
        
        # 等待進程完成或超時
        start_time = time.time()
        while process.is_alive():
            if time.time() - start_time > TIMEOUT:
                print("\n操作超時，強制結束程序...")
                break
            time.sleep(0.1)
            
    except KeyboardInterrupt:
        print("\n接收到中斷信號...")
    finally:
        # 確保進程被終止
        if process.is_alive():
            print("正在強制結束進程...")
            process.terminate()
            process.join(timeout=1)
            if process.is_alive():
                process.kill()
        print("程序已結束")
        sys.exit(0)

if __name__ == "__main__":
    # 設置信號處理
    signal.signal(signal.SIGINT, lambda x, y: sys.exit(0))
    signal.signal(signal.SIGTERM, lambda x, y: sys.exit(0))
    
    main()
