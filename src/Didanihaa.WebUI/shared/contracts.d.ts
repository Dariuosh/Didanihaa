declare namespace Didanihaa {
  interface Source {
    id: string; name: string; storedPath: string; resolvedPath: string; createdUtc: string;
  }
  interface Stream {
    kind: string; codec: string | null; language: string | null;
    width: number | null; height: number | null; channels: number | null;
  }
  interface Metadata {
    status: string; durationSeconds: number | null; title: string | null;
    streams: Stream[]; error: string | null;
  }
  interface Candidate {
    id: string; sourceId: string; relativePath: string; title: string;
    kind: string; mimeType: string; sizeBytes: number; modifiedUtc: string;
    published: boolean; available: boolean; metadata: Metadata;
  }
  interface PublicMedia {
    id: string; title: string; kind: string; mimeType: string;
    sizeBytes: number; durationSeconds: number | null; streamUrl: string; thumbnailUrl?: string | null;
  }
  interface Page<T> { items: T[]; total: number; offset: number; limit: number; }
  interface MediaLocation { sourceId: string; folder: string; name: string; index: number; total: number; }
  interface HomeProfile { id: string; name: string; autoplay: boolean; repeatFolder: boolean; navListLimit?: number; }
  interface PublicPlaylist { sourceId: string; folder: string; name: string; sourceName: string; mediaCount: number; playCount: number; thumbnailUrl: string | null; }
  interface ProfileSession { profile: HomeProfile; favoriteIds: string[]; mutationToken: string; }
  interface MediaFolder { sourceId: string; path: string; name: string; totalFiles: number; directFiles: number; publishedFiles: number; }
  interface FolderListing { sourceId: string | null; path: string; sourceName: string | null; folders: MediaFolder[]; directFiles: number; directAvailableFiles: number; directPublishedFiles: number; }
  interface Job {
    id: string; sourceId: string; status: string; scanned: number; errors: number;
    createdUtc: string; finishedUtc: string | null; message: string | null;
  }
  interface Status {
    statistics: { sources: number; candidates: number; published: number; unavailable: number; };
    service: { supported: boolean; installed: boolean; state: string; executablePath: string | null; };
    mediaOnline: boolean; jobs: Job[];
  }
  interface Session {
    token: string; root: string; publicPort: number;
    viewerUrls: string[];
  }
}
