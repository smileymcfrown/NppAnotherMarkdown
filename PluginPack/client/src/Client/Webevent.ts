import { authHeaders } from "./Session";

export async function notifyWebEvent(name: string, payload: any) {
  await fetch('http://api.example/webevent', {
    method: "POST",
    headers: authHeaders({
      "Content-Type": "application/json"
    }),
    body: JSON.stringify({
      event: name,
      payload
    })
  });
}
