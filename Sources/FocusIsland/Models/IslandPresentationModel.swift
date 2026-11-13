import Combine
import Foundation

enum RecognitionDisclosureDefaults {
    static let applicationsExpanded = false
    static let websitesExpanded = false
}

enum IslandNavigationPage: String, CaseIterable, Identifiable {
    case home
    case settings
    case history
    case recognition
    case browserPermissions
    case historyStats
    case historyDetail

    var id: String { rawValue }
}

@MainActor
final class IslandPresentationModel: ObservableObject {
    @Published var isOnboardingPresented = false
    let reminderScale: CGFloat = 1
    @Published var isShowingRecognitionPicker = false
    let onboardingState = OnboardingState()
    @Published var isExpanded = false
    @Published var isMenuBarPresentation = false
    @Published private(set) var page: IslandNavigationPage = .home
    @Published private(set) var transitionDirection = 1
    @Published private(set) var selectedHistoryDay: HistoryDay?
    @Published var isRecognitionApplicationsExpanded = RecognitionDisclosureDefaults.applicationsExpanded
    @Published var isRecognitionWebsitesExpanded = RecognitionDisclosureDefaults.websitesExpanded
    private var navigationPath: [IslandNavigationPage] = [.home]

    var shouldShowBrowserPermissionEntry: Bool { !isRecognitionWebsitesExpanded }

    func showNavigation() {
        transitionDirection = 1
        navigationPath = [.home]
        page = .home
        selectedHistoryDay = nil
        resetRecognitionDisclosure()
        isExpanded = true
    }

    func collapse() {
        transitionDirection = -1
        navigationPath = [.home]
        isExpanded = false
        page = .home
        selectedHistoryDay = nil
        resetRecognitionDisclosure()
    }

    func open(_ page: IslandNavigationPage) {
        guard page != self.page else { return }
        if page == .recognition { resetRecognitionDisclosure() }
        transitionDirection = 1
        navigationPath.append(page)
        self.page = page
    }

    func goBack() {
        transitionDirection = -1
        guard page != .home else {
            collapse()
            return
        }
        let leavingPage = page
        if navigationPath.count > 1 { navigationPath.removeLast() }
        page = navigationPath.last ?? .home
        if leavingPage == .recognition { resetRecognitionDisclosure() }
        if page != .historyDetail { selectedHistoryDay = nil }
    }

    func resetToNavigationHome() {
        transitionDirection = -1
        navigationPath = [.home]
        page = .home
        selectedHistoryDay = nil
        resetRecognitionDisclosure()
    }

    func openHistoryStats() { open(.historyStats) }
    func openHistoryDetail(_ day: HistoryDay) { selectedHistoryDay = day; open(.historyDetail) }
    func prepareForWebsiteAddition() { isRecognitionWebsitesExpanded = true }

    func resetRecognitionDisclosure() {
        isRecognitionApplicationsExpanded = RecognitionDisclosureDefaults.applicationsExpanded
        isRecognitionWebsitesExpanded = RecognitionDisclosureDefaults.websitesExpanded
    }
}
