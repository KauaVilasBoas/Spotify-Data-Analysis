import { SpotifyMark } from '@/components/brand/SpotifyMark'

/**
 * Attribution is a correctness requirement, not decoration: the data in this project comes from
 * a public Kaggle dataset and never from the Spotify Web API.
 */
export function SiteFooter() {
  return (
    <footer className="mt-20 border-t border-line/70 px-6 py-10 md:px-10">
      <div className="mx-auto flex max-w-[100rem] flex-col gap-6 md:flex-row md:items-start md:justify-between">
        <div className="flex items-start gap-3">
          <SpotifyMark size={26} title="Spotify logo" />
          <div className="space-y-1.5">
            <p className="text-sm font-medium text-text">Spotify Data Analysis</p>
            <p className="max-w-md text-xs leading-relaxed text-text-faint">
              An independent analytics workbench built over the public{' '}
              <span className="text-text-dim">Spotify Tracks Dataset on Kaggle</span>. Data is read
              from that static dataset — never from the Spotify Web API.
            </p>
          </div>
        </div>

        <p className="max-w-sm text-xs leading-relaxed text-text-faint">
          Spotify is a trademark of Spotify AB. This project is not affiliated with, endorsed by,
          sponsored by or connected to Spotify AB in any way.
        </p>
      </div>
    </footer>
  )
}
