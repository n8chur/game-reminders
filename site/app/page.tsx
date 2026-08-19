const PORTABLE_URL =
  "https://github.com/n8chur/game-reminders/releases/download/v0.0.2/GameReminders-0.0.2-win-x64-portable.zip";
const INSTALLER_URL =
  "https://github.com/n8chur/game-reminders/releases/download/v0.0.2/GameReminders-0.0.2-win-x64-setup.exe";
const SHORTCUT_URL =
  "https://github.com/n8chur/game-reminders/releases/download/v0.0.2/Game-Reminder.shortcut";
const ICLOUD_WINDOWS_URL = "https://www.microsoft.com/store/apps/9PKTQ5699M62";

const features = [
  { number: "01", title: "Remember at the right moment", copy: "Write a note now and see it when the matching game launches—not hours before." },
  { number: "02", title: "Add reminders from iPhone", copy: "Use the optional Apple Shortcut to dictate a reminder away from your PC." },
  { number: "03", title: "Sync privately with iCloud", copy: "Your files stay in your iCloud Drive. There is no Game Reminders account or server." },
  { number: "04", title: "Find Steam games automatically", copy: "Scan installed Steam libraries, or add and map any other Windows game manually." },
  { number: "05", title: "Stay out of the way", copy: "The app runs quietly in the notification area and follows your Windows theme." },
  { number: "06", title: "Keep control of completion", copy: "A reminder stays pending until you explicitly dismiss or complete it." },
];

const steps = [
  { number: "1", title: "Download the app", copy: "Use the installer for the easiest setup, or extract the portable ZIP to a permanent folder." },
  { number: "2", title: "Connect iCloud Drive", copy: "On first launch, select the Shortcuts folder inside iCloud Drive and let the app keep its files available offline." },
  { number: "3", title: "Add your games", copy: "Choose Scan Steam, or add a game and its executable manually." },
  { number: "4", title: "Create a reminder", copy: "Make one in the Windows app, or install the Apple Shortcut and dictate it on iPhone. Launch the game to see it." },
];

export default function Home() {
  return (
    <main>
      <header className="site-header">
        <a className="brand" href="#top" aria-label="Game Reminders home">
          <span className="brand-mark" aria-hidden="true">G</span>
          <span>Game Reminders</span>
        </a>
        <nav aria-label="Primary navigation">
          <a href="#features">Features</a>
          <a href="#quick-start">Quick start</a>
          <a href="https://github.com/n8chur/game-reminders">GitHub</a>
        </nav>
      </header>

      <section className="hero" id="top">
        <div className="hero-copy">
          <div className="eyebrow-row">
            <span className="eyebrow">Alpha · v0.0.2</span>
            <span className="platform">Windows 10 &amp; 11 · x64</span>
          </div>
          <h1>Remember what you meant to do <em>when you launch the game.</em></h1>
          <p className="hero-description">
            Save a note on Windows or by voice on iPhone. Game Reminders syncs it through
            iCloud Drive and shows it the next time the right game starts.
          </p>
          <div className="download-area" aria-label="Downloads and requirements">
            <div className="primary-downloads">
              <a className="button button-primary" href={INSTALLER_URL}>
                Download installer
                <span>51.7 MB · recommended</span>
              </a>
              <a className="button button-secondary" href={PORTABLE_URL}>
                Download portable
                <span>71.6 MB · ZIP</span>
              </a>
            </div>
            <div className="sync-downloads">
              <div className="sync-copy">
                <strong>Using the Apple Shortcut?</strong>
                <span>It saves reminder files to iCloud Drive. Install iCloud for Windows so those files can sync to your PC.</span>
              </div>
              <div className="sync-actions">
                <a href={SHORTCUT_URL} aria-label="Download Game-Reminder.shortcut from the v0.0.2 release">Download Game Reminder Shortcut</a>
                <a href={ICLOUD_WINDOWS_URL}>Get iCloud for Windows</a>
              </div>
            </div>
          </div>
          <p className="alpha-note">
            <strong>Alpha software:</strong> packages are currently unsigned, so Windows may
            show a SmartScreen warning. Download only from the official GitHub release.
          </p>
        </div>

      </section>

      <section className="intro-strip" aria-label="Product summary">
        <p>No account</p><span>·</span><p>No telemetry</p><span>·</span>
        <p>No administrator access</p><span>·</span><p>No game injection</p>
      </section>

      <section className="section" id="features">
        <div className="section-heading">
          <p className="kicker">Features</p>
          <h2>A tiny utility with one job.</h2>
          <p>Capture a thought, attach it to a game, and get it back at the useful moment.</p>
        </div>
        <div className="feature-grid">
          {features.map((feature) => (
            <article className="feature" key={feature.number}>
              <span>{feature.number}</span>
              <h3>{feature.title}</h3>
              <p>{feature.copy}</p>
            </article>
          ))}
        </div>
      </section>

      <section className="section quick-start" id="quick-start">
        <div className="section-heading">
          <p className="kicker">Quick start</p>
          <h2>From download to first reminder.</h2>
          <p>You’ll need iCloud for Windows with iCloud Drive enabled to sync Shortcut reminders to the PC.</p>
        </div>
        <ol className="steps">
          {steps.map((step) => (
            <li key={step.number}>
              <span className="step-number">{step.number}</span>
              <div>
                <h3>{step.title}</h3>
                <p>{step.copy}</p>
                {step.number === "4" ? (
                  <a className="text-link" href={SHORTCUT_URL}>Download Game Reminder Shortcut →</a>
                ) : null}
              </div>
            </li>
          ))}
        </ol>
      </section>

      <section className="download-panel">
        <div>
          <p className="kicker">Ready to try it?</p>
          <h2>Make future-you a little more prepared.</h2>
          <p>Free, open source, and currently in alpha.</p>
        </div>
        <div className="download-panel-actions">
          <a className="button button-light" href={INSTALLER_URL}>Download installer</a>
          <a className="release-link" href="https://github.com/n8chur/game-reminders/releases/tag/v0.0.2">View release details</a>
        </div>
      </section>

      <footer>
        <div className="brand footer-brand">
          <span className="brand-mark" aria-hidden="true">G</span>
          <span>Game Reminders</span>
        </div>
        <p>Made for players who remember everything—just slightly too late.</p>
        <a href="https://github.com/n8chur/game-reminders">Source on GitHub</a>
      </footer>
    </main>
  );
}
