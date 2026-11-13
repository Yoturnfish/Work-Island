import ServiceManagement

@MainActor
enum LaunchAtLoginController {
    static func apply(enabled: Bool, settings: TrackingSettings) {
        do {
            if enabled {
                if SMAppService.mainApp.status == .notRegistered || SMAppService.mainApp.status == .notFound {
                    try SMAppService.mainApp.register()
                }
            } else if SMAppService.mainApp.status == .enabled || SMAppService.mainApp.status == .requiresApproval {
                try SMAppService.mainApp.unregister()
            }
            refresh(settings: settings)
        } catch {
            settings.launchAtLoginError = L10n.text("登录启动设置失败：") + error.localizedDescription
        }
    }

    static func refresh(settings: TrackingSettings) {
        let status = SMAppService.mainApp.status
        settings.launchAtLoginRegistered = status == .enabled
        if status == .requiresApproval {
            settings.launchAtLoginError = L10n.text("请在“系统设置 → 通用 → 登录项”中允许 Work Island。")
        } else if settings.launchAtLogin && status != .enabled {
            settings.launchAtLoginError = L10n.text("登录启动尚未生效，请重试或检查系统登录项。")
        } else {
            settings.launchAtLoginError = nil
        }
    }

    static func openSettings() { SMAppService.openSystemSettingsLoginItems() }
}
