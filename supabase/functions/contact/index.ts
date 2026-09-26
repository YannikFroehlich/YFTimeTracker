// Kontaktformular der Website (tracker.yfserver.de/kontakt).
//
// Prueft die Eingaben, bremst Spam, speichert die Nachricht in
// public.contact_messages und schickt sie per SMTP an den Betreiber. "Antworten"
// im Mailprogramm geht direkt an den Absender.
//
// Einrichtung im Supabase-Dashboard (Edge Functions):
// - Funktion "contact" mit diesem Code anlegen, "Verify JWT" AUS: die Website
//   ruft sie ohne Anmeldung auf, und der Publishable Key ist kein JWT.
// - Secret SMTP_PASSWORD = Passwort des Strato-Postfachs webmaster@yfserver.de.
import { SMTPClient } from 'https://deno.land/x/denomailer@1.6.0/mod.ts'
import { createClient } from 'jsr:@supabase/supabase-js@2'

const allowedOrigins = [
  'https://tracker.yfserver.de',
  'http://localhost:5173',
]

// Obergrenzen je Stunde. Schuetzt das Postfach, ohne IP-Adressen zu speichern.
const maxPerHour = 10
const maxPerSenderPerHour = 3
const keepDays = 180

const mail = {
  host: 'smtp.strato.de',
  user: 'webmaster@yfserver.de',
  from: 'YFTimeTracker <no-reply@yfserver.de>',
  to: 'y.froehlich@yfserver.de',
}

/** Zeilenumbrueche und spitze Klammern raus: der Name landet in Mail-Kopfzeilen. */
const headerSafe = (value: string) => value.replace(/[\r\n<>"]/g, ' ').trim()

function secretKey(): string {
  const keys = JSON.parse(Deno.env.get('SUPABASE_SECRET_KEYS') ?? '{}') as Record<string, string>
  return Object.values(keys)[0] ?? Deno.env.get('SUPABASE_SERVICE_ROLE_KEY') ?? ''
}

Deno.serve(async (request) => {
  const origin = request.headers.get('origin') ?? ''
  const cors = {
    'Access-Control-Allow-Origin': allowedOrigins.includes(origin) ? origin : allowedOrigins[0],
    'Access-Control-Allow-Headers': 'authorization, x-client-info, apikey, content-type',
    'Access-Control-Allow-Methods': 'POST, OPTIONS',
    Vary: 'Origin',
  }
  const reply = (status: number, body: Record<string, unknown>) =>
    new Response(JSON.stringify(body), { status, headers: { ...cors, 'Content-Type': 'application/json' } })

  if (request.method === 'OPTIONS') return new Response(null, { headers: cors })
  if (request.method !== 'POST') return reply(405, { error: 'method' })

  let input: Record<string, unknown>
  try {
    input = await request.json()
  } catch {
    return reply(400, { error: 'invalid' })
  }

  // Honeypot: ein fuer Menschen unsichtbares Feld. Bots fuellen es aus und
  // bekommen einen Erfolg vorgespielt, damit sie nicht weiterprobieren.
  if (String(input.website ?? '') !== '') return reply(200, { ok: true })

  const name = headerSafe(String(input.name ?? '')).slice(0, 100)
  const email = String(input.email ?? '').trim().toLowerCase()
  const message = String(input.message ?? '').trim()

  if (
    name.length < 1 ||
    email.length > 200 ||
    !/^[^\s@<>"]+@[^\s@<>"]+\.[^\s@<>"]+$/.test(email) ||
    message.length < 10 ||
    message.length > 5000
  ) {
    return reply(400, { error: 'invalid' })
  }

  const db = createClient(Deno.env.get('SUPABASE_URL')!, secretKey(), { auth: { persistSession: false } })

  // Speicherfrist aus der Datenschutzerklaerung: aeltere Nachrichten gehen weg.
  await db
    .from('contact_messages')
    .delete()
    .lt('created_at', new Date(Date.now() - keepDays * 86_400_000).toISOString())

  const hourAgo = new Date(Date.now() - 3_600_000).toISOString()
  const [total, fromSender] = await Promise.all([
    db.from('contact_messages').select('id', { count: 'exact', head: true }).gte('created_at', hourAgo),
    db
      .from('contact_messages')
      .select('id', { count: 'exact', head: true })
      .gte('created_at', hourAgo)
      .eq('email', email),
  ])
  if (total.error || fromSender.error) return reply(500, { error: 'server' })
  if ((total.count ?? 0) >= maxPerHour || (fromSender.count ?? 0) >= maxPerSenderPerHour) {
    return reply(429, { error: 'rate' })
  }

  const inserted = await db.from('contact_messages').insert({ name, email, message }).select('id').single()
  if (inserted.error) return reply(500, { error: 'server' })

  // Schlaegt der Versand fehl, bleibt die Nachricht gespeichert (delivered =
  // false) und ist im Dashboard lesbar; der Absender bekommt trotzdem Erfolg.
  try {
    const client = new SMTPClient({
      connection: {
        hostname: mail.host,
        port: 465,
        tls: true,
        auth: { username: mail.user, password: Deno.env.get('SMTP_PASSWORD') ?? '' },
      },
    })
    await client.send({
      from: mail.from,
      to: mail.to,
      replyTo: `${name} <${email}>`,
      subject: `Kontaktformular: ${name}`,
      content: `Nachricht über tracker.yfserver.de/kontakt\n\nVon: ${name} <${email}>\n\n${message}`,
    })
    await client.close()
    await db.from('contact_messages').update({ delivered: true }).eq('id', inserted.data.id)
  } catch (error) {
    console.error('Mailversand fehlgeschlagen', error)
  }

  return reply(200, { ok: true })
})
