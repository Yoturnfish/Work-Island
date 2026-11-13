import Foundation

enum AppLanguage: String, CaseIterable, Identifiable {
    case chinese = "zh-Hans"
    case english = "en"

    static let defaultsKey = "interfaceLanguage"
    var id: String { rawValue }
    var title: String { self == .chinese ? "中文" : "English" }

    static func preferredSystemLanguage(from preferredLanguages: [String] = Locale.preferredLanguages) -> Self {
        guard let first = preferredLanguages.first?.lowercased() else { return .english }
        return first.hasPrefix("zh") ? .chinese : .english
    }
}

/// App-specific language selection; does not alter macOS language preferences.
enum L10n {
    static var language: AppLanguage {
        AppLanguage(rawValue: UserDefaults.standard.string(forKey: AppLanguage.defaultsKey) ?? "")
            ?? AppLanguage.preferredSystemLanguage()
    }

    static func text(_ key: String, language: AppLanguage = language) -> String {
        language == .english ? (english[key] ?? key) : key
    }

    static let english: [String: String] = [
        "背景高斯模糊": "Background blur",
        "仅模糊液态玻璃背景，文字和进度条保持清晰；0 为关闭。": "Blur only the Liquid Glass background; text and progress stay crisp. 0 disables blur.",
        "液态玻璃": "Liquid Glass",
        "液态玻璃仅在本机采集窗口背后的小块画面，不保存、不上传；选择透明可恢复原材质。": "Liquid Glass captures only the small area behind this window, locally, without saving or uploading. Select Transparent to restore the original material.",
        "授权屏幕录制 · 启用玻璃": "Allow screen capture · Enable glass",
        "玻璃暂不可用 · 检查录制权限": "Glass unavailable · Check capture permission",
        "请输入非负整数；需要清零时，请明确输入 0。": "Enter non-negative whole numbers. To reset the total, enter 0 explicitly.",
        "时长数值过大，请输入较小的数值。": "The duration is too large. Enter a smaller value.",
        "磨砂度": "Frosting",
        "调节系统磨砂材质，并用中性暗色减轻发白；不是精确高斯模糊半径。": "Blend system frosting with neutral shading to reduce whitening; not an exact Gaussian blur radius.",
        "背景样式": "Background",
        "模拟玻璃": "Simulated Glass",
        "透明": "Transparent",
        "黑色": "Black",
        "模糊强度": "Blur strength",
        "调节清透与磨砂材质的混合强度，不是精确的高斯模糊半径。": "Blend clear and frosted materials; this is not an exact Gaussian blur radius.",
        "当前应用程序": "Current apps",
        "填写路径中的关键词，即可自动识别": "Enter a keyword from the URL to recognize the website automatically.",
        "系统登录项": "System Login Items",
        "登录启动尚未生效，请重试或检查系统登录项。": "Launch at login is not active. Retry or check System Login Items.",
        "浅粉色": "Soft pink",
        "切换颜色可重新配置识别组合，适应不同工作类型。": "Switch colors to configure separate tracking groups for different kinds of work.",
        "默认": "Default", "命名": "Name", "为颜色组合命名": "Name this color group",
        "网址不可用 · 请检查授权": "URL unavailable · Check permission",
        "黄色": "Yellow", "切换颜色组合": "Switch color group",
        "自动识别": "Auto-detect", "主题模式": "Theme mode", "当前网页": "Current website",
        "没有运行的支持浏览器": "No supported browser running", "编辑主题": "Edit theme",
        "手动添加": "Add manually", "网站": "Websites", "已添加关键词：": "Added keyword: ",
        "无法读取当前网页": "Cannot read current website",
        "请在目标浏览器最前方窗口打开普通网页，并在浏览器访问权限中授权。不会读取后台标签页。": "Open a regular web page in the browser's front window and authorize Browser access. Background tabs are not read.",
        "滚动设置": "Scroll settings",
        "组合已删除 · 停止识别": "Deleted theme · Not tracking",
        "蓝色": "Blue", "紫色": "Purple", "青色": "Cyan", "粉色": "Pink", "橙色": "Orange", "绿色": "Green",
        "新组合": "New theme", "主题限定": "Theme mode", "关闭 · 默认规则": "Off · Default rules",
        "新建并启用组合": "Create and activate theme", "组合名称": "Theme name", "主题色": "Theme color",
        "删除组合": "Delete theme", "删除组合？": "Delete theme?",
        "历史记录保留，默认规则不会自动启用。": "History is kept. Default rules will not be enabled automatically.",
        "仅下方规则计时，可重复添加": "Only rules below count; duplicates allowed",
        "暂无运行中的应用": "No running applications", "自动添加 · 运行中的应用": "Auto-add · Running apps",
        "进度条样式": "Progress style", "矩形矩阵": "Pixel matrix", "流畅进度条": "Continuous", "分段进度条": "Segments", "波形进度条": "Waveform",
        "细线进度条": "Thin line", "胶囊进度条": "Capsules",
        "超级提醒": "Super reminder",
        "游离计时": "Away timer", "关": "Off", "开": "On", "闪光": "Flash",
        "进入游离后显示本次时长，恢复工作即归零；不累计、不保存。": "Show the current away session; reset on return. Never accumulated or saved.",
        "游离一轮后中心渐变闪烁，逐轮加快，最快每两秒三次。": "A soft center-out pulse after one away interval, gradually faster, up to three pulses every two seconds.",
        "新手引导": "Getting started",
        "本地数据存储初始化失败：": "Local storage could not be initialized: ",
        "登录启动设置失败：": "Could not set launch at login: ",
        "计时数据无法保存或读取：": "Could not save or read time records: ",
        "。当前时段已保留在内存中，请检查磁盘空间和文件权限后重试。": ". The current session is kept in memory. Check disk space and file permissions, then retry.",
        "返回计时页面": "Back to timer",
        "设置": "Settings",
        "历史": "History",
        "识别": "Tracking",
        "继续计时": "Resume",
        "暂停计时": "Pause",
        "返回导航": "Back",
        "登录后启动": "Launch at login",
        "显示模式": "Display mode",
        "时间格式": "Time format",
        "每日目标": "Daily goal",
        "进入游离状态": "Idle after",
        "窗口尺寸": "Window size",
        "标准": "Standard",
        "每日刷新": "Day starts at",
        "24 小时制": "24-hour",
        "透明度": "Opacity",
        "退出 Work Island": "Quit Work Island",
        "开发版": "Dev",
        "历史统计": "Statistics",
        "还没有创作记录": "No work recorded yet",
        "该日期仅保留总时长": "Daily total only",
        "修改后回车保存": "Press Return to save",
        "时": "h",
        "分": "m",
        "应用程序": "Applications",
        "添加": "Add",
        "网页路径": "Web paths",
        "浏览器访问权限": "Browser access",
        "路径包含，如 bilibili.com/video": "URL contains, e.g. github.com",
        "新路径": "New path",
        "选择需要识别的应用": "Choose an app to track",
        "读取当前网页路径": "Read current URL",
        "授权": "Authorize",
        "前往系统自动化设置": "Open Automation settings",
        "已开启": "On",
        "已关闭": "Off",
        "计时记录未保存": "Time records not saved",
        "知道了": "OK",
        "屏幕工作时间": "Work time",
        "打开导航": "Open navigation",
        "今日目标进度": "Daily goal progress",
        "悬浮窗模式": "Floating",
        "菜单栏模式": "Menu bar",
        "左侧": "Left",
        "居中": "Center",
        "右侧": "Right",
        "未安装": "Not installed",
        "待授权": "Not authorized",
        "已授权": "Authorized",
        "已拒绝": "Denied",
        "结束时间必须晚于开始时间。": "End time must be after start time.",
        "一次记录不能跨越两个统计日。": "A record cannot span two tracking days.",
        "超过七天的记录已归档为日总时长，不能再修改。": "Records older than seven days are archived as daily totals and cannot be edited.",
        "请在“系统设置 → 通用 → 登录项”中允许 Work Island。": "Allow Work Island in System Settings > General > Login Items.",
        "Work Island 无法启动": "Work Island could not start",
        "语言": "Language",
        "小时": "hours",
        "分钟": "min",
        "秒": "sec",
    ]
}
