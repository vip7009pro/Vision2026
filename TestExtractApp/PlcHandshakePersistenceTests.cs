using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using VisionInspectionApp.Application.PLC.Services;
using VisionInspectionApp.Models;
using VisionInspectionApp.UI.ViewModels.PLC;

namespace TestExtractApp;

public static class PlcHandshakePersistenceTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("\n=======================================================");
        Console.WriteLine("BẮT ĐẦU KIỂM THỬ: BẢO TOÀN CẤU HÌNH HANDSHAKE 24/7 & SAFE GUARD");
        Console.WriteLine("=======================================================");

        Test1_IndustrialHandshakeConfig_Helpers_And_Defaults();
        Test2_SaveGlobalConfig_SafeGuard_Prevents_Empty_Overwrite();
        Test3_LoadGlobalConfig_Auto_Recover_From_Backup_Or_Seed();
        Test4_EnsureIndustrialTagsRegistered_Populates_Tags();
        Test5_ViewModel_ResetCommands_And_TagList_Integrity();
        Test6_EnsureTagRegistered_ImageSourceTriggerTag();
        Test7_GetEffectivePlcAckTag_And_ThreadSafeConfigUpdate();

        Console.WriteLine("=======================================================");
        Console.WriteLine("TẤT CẢ 7/7 TEST BẢO TOÀN CẤU HÌNH HANDSHAKE, NON-BLOCKING & THREAD SAFETY ĐÃ VƯỢT QUA (PASSED 100%)");
        Console.WriteLine("=======================================================\n");
    }

    private static void Test1_IndustrialHandshakeConfig_Helpers_And_Defaults()
    {
        Console.WriteLine("--> Test 1: IndustrialHandshakeConfig Helper Methods & Defaults");

        var cfg = new IndustrialHandshakeConfig();
        // Mặc định ban đầu có tag Y1..Y5
        if (!cfg.HasConfiguredTags())
            throw new Exception("Mặc định HasConfiguredTags phải là true.");

        // Thử xóa trắng
        cfg.ReadyTagName = "";
        cfg.BusyTagName = "";
        cfg.DoneTagName = "";
        cfg.PassTagName = "";
        cfg.NgTagName = "";
        cfg.PlcAckTagName = "";
        if (cfg.HasConfiguredTags())
            throw new Exception("Khi tất cả các tag rỗng, HasConfiguredTags phải trả về false.");

        // Áp dụng chuẩn FX5U
        cfg.ApplyFx5uDefaults("PLC_2");
        if (cfg.PlcId != "PLC_2" || cfg.ReadyTagName != "M101" || cfg.BusyTagName != "M102" ||
            cfg.DoneTagName != "M103" || cfg.PassTagName != "M104" || cfg.NgTagName != "M105" ||
            cfg.PassSignalType != PlcSignalType.RisingEdge || cfg.PulseDurationMs != 20 ||
            !cfg.NonBlockingMode || cfg.QueueRegisterStart != "M200")
        {
            throw new Exception("ApplyFx5uDefaults gán sai giá trị chuẩn Mitsubishi FX5U!");
        }

        // Áp dụng chuẩn I/O
        cfg.ApplyStandardIoDefaults("PLC1");
        if (cfg.PlcId != "PLC1" || cfg.ReadyTagName != "Y1_VisionReady" || cfg.PassTagName != "Y4_VisionPass" ||
            cfg.PassSignalType != PlcSignalType.Level || cfg.PulseDurationMs != 100)
        {
            throw new Exception("ApplyStandardIoDefaults gán sai giá trị chuẩn I/O!");
        }

        Console.WriteLine("    [OK] Helper methods và Defaults chuẩn hóa chính xác 100%.");
    }

    private static void Test2_SaveGlobalConfig_SafeGuard_Prevents_Empty_Overwrite()
    {
        Console.WriteLine("--> Test 2: Safe Guard của SaveGlobalConfig chống ghi đè rỗng khi RAM bị mất tag");

        string tempConfigPath = TestPlcConfigHelper.CreateIsolatedTestConfigPath();
        try
        {
            // 1. Tạo file cấu hình ban đầu với tag hợp lệ M101..M105
            var initialContainer = new PlcConfigContainer
            {
                Plcs = new() { new PlcModel { Id = "P1", Name = "PLC1", DriverType = PlcDriverType.Mitsubishi, Enabled = true } },
                IndustrialConfig = new PlcIndustrialConfig()
            };
            initialContainer.IndustrialConfig.Handshake.ApplyFx5uDefaults("PLC1");
            string initialJson = JsonSerializer.Serialize(initialContainer, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(tempConfigPath, initialJson);

            // 2. Khởi tạo PlcManagerService nạp file cấu hình trên
            using var service = new PlcManagerService(tempConfigPath);
            if (service.IndustrialConfig.Handshake.ReadyTagName != "M101")
            {
                throw new Exception($"Cấu hình nạp ban đầu không khớp: mong đợi M101, nhận {service.IndustrialConfig.Handshake.ReadyTagName}");
            }

            // 3. Giả lập lỗi ComboBox hoặc người dùng xóa trắng toàn bộ tag trong RAM
            service.IndustrialConfig.Handshake.ReadyTagName = "";
            service.IndustrialConfig.Handshake.BusyTagName = "";
            service.IndustrialConfig.Handshake.DoneTagName = "";
            service.IndustrialConfig.Handshake.PassTagName = "";
            service.IndustrialConfig.Handshake.NgTagName = "";
            service.IndustrialConfig.Handshake.PlcAckTagName = "";
            service.IndustrialConfig.Handshake.ResetQueueTagName = "";

            // 4. Gọi SaveGlobalConfig()
            service.SaveGlobalConfig();

            // 5. Kiểm tra file trên đĩa: Safe Guard phải bảo toàn tag M101..M105 từ đĩa cũ
            string savedJson = File.ReadAllText(tempConfigPath);
            var reloaded = JsonSerializer.Deserialize<PlcConfigContainer>(savedJson);
            if (reloaded?.IndustrialConfig?.Handshake == null)
            {
                throw new Exception("Lỗi: IndustrialConfig.Handshake bị null sau khi lưu!");
            }

            if (reloaded.IndustrialConfig.Handshake.ReadyTagName != "M101" ||
                reloaded.IndustrialConfig.Handshake.PassTagName != "M104" ||
                reloaded.IndustrialConfig.Handshake.NgTagName != "M105")
            {
                throw new Exception($"Safe Guard thất bại: Các tag bị ghi đè thành rỗng! Ready={reloaded.IndustrialConfig.Handshake.ReadyTagName}");
            }

            // Kiểm tra file backup .backup đã được tạo tự động
            if (!File.Exists(tempConfigPath + ".backup"))
            {
                throw new Exception("File backup .backup không được tạo trước khi ghi đè!");
            }

            Console.WriteLine("    [OK] Safe Guard đã kích hoạt thành công, bảo toàn 100% tag Handshake chống xóa trắng.");
        }
        finally
        {
            try { File.Delete(tempConfigPath); } catch { }
            try { File.Delete(tempConfigPath + ".backup"); } catch { }
        }
    }

    private static void Test3_LoadGlobalConfig_Auto_Recover_From_Backup_Or_Seed()
    {
        Console.WriteLine("--> Test 3: LoadGlobalConfig tự động phục hồi Handshake từ file backup khi file chính bị rỗng");

        string tempConfigPath = TestPlcConfigHelper.CreateIsolatedTestConfigPath();
        string tempBackupPath = tempConfigPath + ".backup";
        try
        {
            // 1. Tạo file chính có Handshake tags bị rỗng
            var brokenContainer = new PlcConfigContainer
            {
                Plcs = new() { new PlcModel { Id = "P1", Name = "PLC1", DriverType = PlcDriverType.Mitsubishi, Enabled = true } },
                IndustrialConfig = new PlcIndustrialConfig()
            };
            brokenContainer.IndustrialConfig.Handshake.ReadyTagName = "";
            brokenContainer.IndustrialConfig.Handshake.BusyTagName = "";
            brokenContainer.IndustrialConfig.Handshake.DoneTagName = "";
            brokenContainer.IndustrialConfig.Handshake.PassTagName = "";
            brokenContainer.IndustrialConfig.Handshake.NgTagName = "";
            brokenContainer.IndustrialConfig.Handshake.PlcAckTagName = "";
            brokenContainer.IndustrialConfig.Handshake.ResetQueueTagName = "";
            File.WriteAllText(tempConfigPath, JsonSerializer.Serialize(brokenContainer));

            // 2. Tạo file backup có Handshake tags hợp lệ M101..M105
            var backupContainer = new PlcConfigContainer
            {
                Plcs = new() { new PlcModel { Id = "P1", Name = "PLC1", DriverType = PlcDriverType.Mitsubishi, Enabled = true } },
                IndustrialConfig = new PlcIndustrialConfig()
            };
            backupContainer.IndustrialConfig.Handshake.ApplyFx5uDefaults("PLC1");
            File.WriteAllText(tempBackupPath, JsonSerializer.Serialize(backupContainer));

            // 3. Khởi tạo PlcManagerService nạp file cấu hình
            using var service = new PlcManagerService(tempConfigPath);

            // 4. Kiểm tra service đã tự động khôi phục từ file backup chưa
            if (service.IndustrialConfig.Handshake.ReadyTagName != "M101" ||
                service.IndustrialConfig.Handshake.PassTagName != "M104" ||
                service.IndustrialConfig.Handshake.NgTagName != "M105")
            {
                throw new Exception($"Tự động phục hồi thất bại: Ready={service.IndustrialConfig.Handshake.ReadyTagName}");
            }

            Console.WriteLine("    [OK] Tự động phát hiện và phục hồi cấu hình Handshake từ file backup thành công 100%.");
        }
        finally
        {
            try { File.Delete(tempConfigPath); } catch { }
            try { File.Delete(tempBackupPath); } catch { }
        }
    }

    private static void Test4_EnsureIndustrialTagsRegistered_Populates_Tags()
    {
        Console.WriteLine("--> Test 4: EnsureIndustrialTagsRegistered tự động đăng ký tag bắt tay vào danh mục Tags");

        string tempConfigPath = TestPlcConfigHelper.CreateIsolatedTestConfigPath();
        try
        {
            using var service = new PlcManagerService(tempConfigPath);
            service.IndustrialConfig.Handshake.ApplyFx5uDefaults("PLC1");

            // Xóa hết tags để kiểm tra tự động add
            service.Tags.Clear();

            service.EnsureIndustrialTagsRegistered();

            // Kiểm tra các tag M101..M105 đã có trong service.Tags
            var tagNames = service.Tags.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!tagNames.Contains("M101") || !tagNames.Contains("M102") || !tagNames.Contains("M103") ||
                !tagNames.Contains("M104") || !tagNames.Contains("M105"))
            {
                throw new Exception("EnsureIndustrialTagsRegistered không đăng ký đủ các tag M101..M105!");
            }

            Console.WriteLine("    [OK] Các tag Handshake M101..M105 đã được đăng ký tự động vào danh mục Tags 100%.");
        }
        finally
        {
            try { File.Delete(tempConfigPath); } catch { }
            try { File.Delete(tempConfigPath + ".backup"); } catch { }
        }
    }

    private static void Test5_ViewModel_ResetCommands_And_TagList_Integrity()
    {
        Console.WriteLine("--> Test 5: ViewModel ResetHandshakeFx5uCommand & RefreshAvailableTagsAndPlcs bảo toàn");

        string tempConfigPath = TestPlcConfigHelper.CreateIsolatedTestConfigPath();
        try
        {
            using var service = new PlcManagerService(tempConfigPath);
            var plc = new PlcModel { Id = "PLC_TEST", Name = "PLC_TEST", DriverType = PlcDriverType.Mitsubishi, Enabled = true };
            service.Plcs.Add(plc);

            var vm = new PlcManagerViewModel(service);

            // Kiểm tra danh mục AvailableTagNames có sẵn M101..M105
            if (!vm.AvailableTagNames.Contains("M101") || !vm.AvailableTagNames.Contains("M104"))
            {
                throw new Exception("AvailableTagNames trong ViewModel thiếu địa chỉ M101..M105!");
            }

            // Test Reset FX5U
            vm.ExecuteResetHandshakeFx5u(showDialog: false);
            if (vm.Handshake.ReadyTagName != "M101" || vm.Handshake.PassTagName != "M104" ||
                vm.Handshake.PassSignalType != PlcSignalType.RisingEdge)
            {
                throw new Exception("ResetHandshakeFx5u trong ViewModel gán sai cấu hình!");
            }

            // Test Reset Standard I/O
            vm.ExecuteResetHandshakeStandardIo(showDialog: false);
            if (vm.Handshake.ReadyTagName != "Y1_VisionReady" || vm.Handshake.PassTagName != "Y4_VisionPass" ||
                vm.Handshake.PassSignalType != PlcSignalType.Level)
            {
                throw new Exception("ResetHandshakeStandardIo trong ViewModel gán sai cấu hình!");
            }

            Console.WriteLine("    [OK] ViewModel Commands và AvailableTagNames bảo toàn danh mục và thiết lập chính xác 100%.");
        }
        finally
        {
            try { File.Delete(tempConfigPath); } catch { }
            try { File.Delete(tempConfigPath + ".backup"); } catch { }
        }
    }

    private static void Test6_EnsureTagRegistered_ImageSourceTriggerTag()
    {
        Console.WriteLine("--> Test 6: EnsureTagRegistered tự động đăng ký tag M10 vào danh mục Tags và Polling Engine");

        string tempConfigPath = TestPlcConfigHelper.CreateIsolatedTestConfigPath();
        try
        {
            using var service = new PlcManagerService(tempConfigPath);
            service.Plcs.Add(new PlcModel { Id = "plc_2", Name = "PLC_2", DriverType = PlcDriverType.Mitsubishi, Enabled = true });

            // 1. Kiểm tra ban đầu M10 chưa có trong Tags
            bool initiallyHasM10 = service.Tags.Any(t => t.Address == "M10" && (t.PlcId == "PLC_2" || t.PlcId == "plc_2"));
            if (initiallyHasM10)
            {
                throw new Exception("Lỗi test: M10 không nên có sẵn trong PLC_2");
            }

            // 2. Gọi EnsureTagRegistered cho M10 trên PLC_2
            bool added = service.EnsureTagRegistered("PLC_2", "M10", PlcDataType.Bool, "ImageSource Trigger M10", "Trigger");
            if (!added)
            {
                throw new Exception("EnsureTagRegistered trả về false khi thêm tag mới M10!");
            }

            // 3. Kiểm tra M10 đã có trong Tags
            var registeredTag = service.Tags.FirstOrDefault(t => t.Address == "M10");
            if (registeredTag == null || registeredTag.DataType != PlcDataType.Bool)
            {
                throw new Exception("Tag M10 chưa được thêm chính xác vào danh mục Tags!");
            }

            // 4. Kiểm tra GetAllTagsToPoll() tự động chứa M10
            var allPollTags = service.GetAllTagsToPoll();
            if (!allPollTags.Any(t => t.Address == "M10"))
            {
                throw new Exception("GetAllTagsToPoll() không chứa tag M10!");
            }

            // 5. Gọi lại EnsureTagRegistered lần 2: Không được trùng lặp
            bool addedDuplicate = service.EnsureTagRegistered("PLC_2", "M10", PlcDataType.Bool);
            if (addedDuplicate)
            {
                throw new Exception("EnsureTagRegistered không chống được trùng lặp tag M10!");
            }

            Console.WriteLine("    [OK] EnsureTagRegistered đăng ký M10 vào Tags và PollingEngine thành công 100%, chống trùng lặp hoàn hảo.");
        }
        finally
        {
            try { File.Delete(tempConfigPath); } catch { }
            try { File.Delete(tempConfigPath + ".backup"); } catch { }
        }
    }

    private static void Test7_GetEffectivePlcAckTag_And_ThreadSafeConfigUpdate()
    {
        Console.WriteLine("--> Test 7: GetEffectivePlcAckTag trả về null khi rỗng và Thread Safety bảo vệ UI ViewModel");

        // 1. Kiểm tra IndustrialHandshakeStateMachine: Bỏ Ack (PlcAckTagName = "") phải trả về null
        string tempConfigPath = TestPlcConfigHelper.CreateIsolatedTestConfigPath();
        try
        {
            using var plcService = new PlcManagerService(tempConfigPath);
            var plcModel = new PlcModel { Id = "PLC_2", Name = "PLC_2", DriverType = PlcDriverType.Mitsubishi, Enabled = true };
            plcService.Plcs.Add(plcModel);

            // Cấu hình Non-blocking và rỗng PlcAckTagName
            plcService.IndustrialConfig.Handshake.PlcId = "PLC_2";
            plcService.IndustrialConfig.Handshake.NonBlockingMode = true;
            plcService.IndustrialConfig.Handshake.PlcAckTagName = "";

            var sm = new IndustrialHandshakeStateMachine(plcService, "PLC_2");
            sm.ApplyConfig(plcService.IndustrialConfig.Handshake);

            var ackTag = sm.GetEffectivePlcAckTag();
            if (!string.IsNullOrEmpty(ackTag))
            {
                throw new Exception($"Lỗi: Khi PlcAckTagName rỗng, GetEffectivePlcAckTag phải trả về null, nhưng nhận được: '{ackTag}'");
            }

            // Cấu hình có tag Ack rõ ràng
            plcService.IndustrialConfig.Handshake.PlcAckTagName = "M10";
            sm.ApplyConfig(plcService.IndustrialConfig.Handshake);
            plcService.EnsureTagRegistered("PLC_2", "M10", PlcDataType.Bool);
            var ackTagConfigured = sm.GetEffectivePlcAckTag();
            if (ackTagConfigured != "M10")
            {
                throw new Exception($"Lỗi: Khi PlcAckTagName='M10', GetEffectivePlcAckTag phải trả về M10, nhưng nhận được: '{ackTagConfigured}'");
            }

            // 2. Kiểm tra ViewModel không ném InvalidOperationException khi PlcModel.State đổi trên ThreadPool
            var vm = new PlcManagerViewModel(plcService);
            vm.SelectedPlc = plcModel;

            var threadEx = (Exception?)null;
            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    // Giả lập driver cập nhật trạng thái kết nối trên background thread (như MitsubishiDriver làm)
                    plcModel.State = PlcConnectionState.Connected;
                    plcModel.CpuName = "FX5U";
                    plcModel.State = PlcConnectionState.Disconnected;
                }
                catch (Exception ex)
                {
                    threadEx = ex;
                }
            });
            thread.Start();
            thread.Join();

            if (threadEx != null)
            {
                throw new Exception($"Lỗi Thread Safety: Cập nhật PlcModel từ background thread làm văng lỗi UI: {threadEx.Message}", threadEx);
            }

            Console.WriteLine("    [OK] GetEffectivePlcAckTag trả về null khi bỏ Ack và DispatchOnUiThread bảo vệ UI an toàn 100%.");
        }
        finally
        {
            try { File.Delete(tempConfigPath); } catch { }
            try { File.Delete(tempConfigPath + ".backup"); } catch { }
        }
    }
}
