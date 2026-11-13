import AppKit
import Foundation

// Resolve only this product's registered instances; never terminate by process name.
let source = URL(fileURLWithPath: CommandLine.arguments[1]).standardizedFileURL
let target = URL(fileURLWithPath: "/Applications/Work Island.app")
let fm = FileManager.default
if fm.fileExists(atPath: target.path) && Bundle(url: target)?.bundleIdentifier != "com.focusisland.app" {
    fatalError("Destination belongs to another application; nothing changed")
}
// Keep transient bundles outside /Applications so LaunchServices and Spotlight
// never discover them as additional installed copies.
let staging = fm.temporaryDirectory.appendingPathComponent("WorkIsland-update-\(UUID().uuidString)")
try fm.createDirectory(at: staging, withIntermediateDirectories: false)
var upgradeSucceeded = false
defer { if upgradeSucceeded { try? fm.removeItem(at: staging) } }
// Temporary copies deliberately do not use the .app extension. LaunchServices
// may register any .app placed under /Applications, even when it exists only
// during an atomic update, which leaves a duplicate Spotlight result after the
// directory has been removed.
let candidate = staging.appendingPathComponent("Candidate.bundle")
let backup = staging.appendingPathComponent("Previous.backup")
try fm.copyItem(at: source, to: candidate)
guard let bundle = Bundle(url: candidate), bundle.bundleIdentifier == "com.focusisland.app",
      let version = bundle.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String,
      let build = bundle.object(forInfoDictionaryKey: "CFBundleVersion") as? String else {
    fatalError("Invalid Work Island bundle")
}
let verification = Process()
verification.executableURL = URL(fileURLWithPath: "/usr/bin/codesign")
verification.arguments = ["--verify", "--deep", "--strict", candidate.path]
try verification.run()
verification.waitUntilExit()
guard verification.terminationStatus == 0 else { fatalError("Signature failed; old app left running") }
let previous = NSRunningApplication.runningApplications(withBundleIdentifier: "com.focusisland.app")
for instance in previous {
    guard let path = instance.bundleURL, let executable = instance.executableURL,
          executable.lastPathComponent == "FocusIsland", path.pathExtension == "app" else {
        fatalError("Unable to identify old instance safely")
    }
    print("Quit PID \(instance.processIdentifier): \(path.path)")
    guard instance.terminate() else { fatalError("Old instance refused normal quit") }
}
let deadline = Date().addingTimeInterval(15)
while previous.contains(where: { !$0.isTerminated }) && Date() < deadline {
    RunLoop.current.run(until: Date().addingTimeInterval(0.1))
}
guard previous.allSatisfy({ $0.isTerminated }) else { fatalError("Old instance still running; no duplicate launch") }
func restorePrevious(_ reason: String) -> Never {
    let replacements = NSRunningApplication.runningApplications(withBundleIdentifier: "com.focusisland.app")
        .filter { $0.bundleURL?.standardizedFileURL.path == target.path }
    for app in replacements { _ = app.terminate() }
    let stopDeadline = Date().addingTimeInterval(15)
    while replacements.contains(where: { !$0.isTerminated }) && Date() < stopDeadline {
        RunLoop.current.run(until: Date().addingTimeInterval(0.1))
    }
    if replacements.allSatisfy({ $0.isTerminated }) {
        do {
            if fm.fileExists(atPath: backup.path) {
                if fm.fileExists(atPath: target.path) { try fm.moveItem(at: target, to: staging.appendingPathComponent("Failed.backup")) }
                try fm.moveItem(at: backup, to: target)
                NSWorkspace.shared.openApplication(at: target, configuration: .init())
            } else if let oldURL = previous.first?.bundleURL, fm.fileExists(atPath: oldURL.path) {
                NSWorkspace.shared.openApplication(at: oldURL, configuration: .init())
            }
            RunLoop.current.run(until: Date().addingTimeInterval(2))
        } catch { print("Recovery needs attention: \(error). Backup: \(backup.path)") }
    }
    fatalError("\(reason). Recovery files preserved at \(staging.path)")
}
do {
    if fm.fileExists(atPath: target.path) { try fm.moveItem(at: target, to: backup) }
    try fm.moveItem(at: candidate, to: target)
} catch {
    restorePrevious(error.localizedDescription)
}
let config = NSWorkspace.OpenConfiguration()
config.activates = true
var done = false
var failure: String?
NSWorkspace.shared.openApplication(at: target, configuration: config) { app, error in
    if let error { failure = error.localizedDescription }
    else if let app, app.bundleURL?.standardizedFileURL.path == target.path {
        print("Started PID \(app.processIdentifier): \(target.path), version \(version)")
    } else { failure = "Unexpected running application path" }
    done = true
}
let launchDeadline = Date().addingTimeInterval(20)
while !done && Date() < launchDeadline { RunLoop.current.run(until: Date().addingTimeInterval(0.1)) }
guard done else { restorePrevious("Timed out waiting for application launch") }
if let failure { restorePrevious(failure) }
RunLoop.current.run(until: Date().addingTimeInterval(2))
guard NSRunningApplication.runningApplications(withBundleIdentifier: "com.focusisland.app").contains(where: {
    !$0.isTerminated && $0.bundleURL?.standardizedFileURL.path == target.path
}) else { restorePrevious("New application exited after launch") }
let installedInfo = try PropertyListSerialization.propertyList(from: Data(contentsOf: target.appendingPathComponent("Contents/Info.plist")), format: nil) as? [String: Any]
guard installedInfo?["CFBundleShortVersionString"] as? String == version,
      installedInfo?["CFBundleVersion"] as? String == build else {
    restorePrevious("Installed version mismatch")
}
upgradeSucceeded = true
print("Verified running Work Island \(version) (\(build)) at \(target.path)")
