let state: Didanihaa.ProfileSession | null = null;
export async function recordPlayback(mediaId: string): Promise<void> {
  if (!state) await loadHomeProfile();
  if (state!.profile.navListLimit === undefined) return;
  const send = () => fetch('/api/profile/playback', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Didanihaa-Profile': state!.mutationToken }, body: JSON.stringify({ mediaId }) });
  let response = await send(); if (response.status === 401) { await loadHomeProfile(); response = await send(); }
  if (!response.ok) throw new Error('ثبت آمار پخش انجام نشد.');
}
export async function loadHomeProfile(): Promise<Didanihaa.ProfileSession> {
  const response = await fetch('/api/profile', { cache: 'no-store' });
  if (!response.ok) throw new Error('پروفایل خانه در دسترس نیست.');
  state = await response.json() as Didanihaa.ProfileSession; return state;
}
export async function saveProfile(path: 'favorites' | 'preferences', body: unknown): Promise<Didanihaa.ProfileSession> {
  if (!state) await loadHomeProfile();
  let response = await fetch(`/api/profile/${path}`, { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Didanihaa-Profile': state!.mutationToken }, body: JSON.stringify(body) });
  if (response.status === 401) {
    await loadHomeProfile(); response = await fetch(`/api/profile/${path}`, { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-Didanihaa-Profile': state!.mutationToken }, body: JSON.stringify(body) });
  }
  if (!response.ok) { const error = await response.json(); throw new Error(error.error || 'ذخیرهٔ پروفایل انجام نشد.'); }
  return loadHomeProfile();
}
