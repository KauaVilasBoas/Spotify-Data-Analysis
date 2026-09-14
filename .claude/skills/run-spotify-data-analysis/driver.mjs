// Driver de browser para a SPA, sem nenhuma dependencia externa.
// Usa o Chrome ja instalado na maquina e o WebSocket nativo do Node 22+
// para falar CDP. Nao instala nada, nao baixa browser.
//
//   node driver.mjs shot  <url> <saida.png>
//   node driver.mjs click <url> <textoDoBotao> <saida.png>
//   node driver.mjs text  <url> [textoDoBotao]
//
// "click" existe porque a estrategia do recomendador (Content/Blend) mora em
// useState e NAO na URL: sem clicar, so da para ver o caminho content.

import { spawn, execFileSync } from 'node:child_process'
import { mkdtempSync, rmSync, writeFileSync, existsSync } from 'node:fs'
import { createServer } from 'node:net'
import { tmpdir } from 'node:os'
import { join } from 'node:path'

const CHROME_CANDIDATES = [
  process.env.CHROME_PATH,
  'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
  'C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe',
  'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
  'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe',
  '/usr/bin/google-chrome',
  '/usr/bin/chromium',
].filter(Boolean)

const NAV_WAIT_MS = Number(process.env.NAV_WAIT_MS ?? 8000)
const CLICK_WAIT_MS = Number(process.env.CLICK_WAIT_MS ?? 5000)

const sleep = (ms) => new Promise((r) => setTimeout(r, ms))

// Porta livre por execucao. Porta fixa e armadilha: no Windows o Chrome deixa
// processos filhos vivos depois do kill do lancador, e a execucao seguinte se
// conecta ao Chrome ZUMBI da anterior em vez do novo. O sintoma e um
// screenshot quase em branco com o DOM vazio.
function freePort() {
  return new Promise((resolve, reject) => {
    const srv = createServer()
    srv.on('error', reject)
    // listen e assincrono: address() so tem porta depois do evento 'listening'.
    srv.listen(0, '127.0.0.1', () => {
      const { port } = srv.address()
      srv.close(() => resolve(port))
    })
  })
}

// child.kill() no Windows nao alcanca a arvore de processos do Chrome.
function killTree(pid) {
  try {
    if (process.platform === 'win32') {
      execFileSync('taskkill', ['/PID', String(pid), '/T', '/F'], { stdio: 'ignore' })
    } else {
      process.kill(-pid, 'SIGKILL')
    }
  } catch {
    // ja morreu
  }
}

const CDP_PORT = Number(process.env.CDP_PORT ?? 0) || (await freePort())

function findChrome() {
  const hit = CHROME_CANDIDATES.find((p) => existsSync(p))
  if (!hit) {
    console.error('Nenhum Chrome/Edge encontrado. Defina CHROME_PATH.')
    process.exit(2)
  }
  return hit
}

async function waitForCdp(deadlineMs = 30000) {
  const stop = Date.now() + deadlineMs
  while (Date.now() < stop) {
    try {
      const r = await fetch(`http://127.0.0.1:${CDP_PORT}/json/version`)
      if (r.ok) return true
    } catch {
      // ainda subindo
    }
    await sleep(400)
  }
  return false
}

function connect() {
  let id = 0
  const pending = new Map()
  let ws
  return {
    async open() {
      const targets = await (await fetch(`http://127.0.0.1:${CDP_PORT}/json/list`)).json()
      const page = targets.find((t) => t.type === 'page')
      if (!page) throw new Error('Nenhum target do tipo page no CDP.')
      ws = new WebSocket(page.webSocketDebuggerUrl)
      await new Promise((res, rej) => {
        ws.onopen = res
        ws.onerror = rej
      })
      ws.onmessage = (ev) => {
        const m = JSON.parse(ev.data)
        if (m.id && pending.has(m.id)) {
          pending.get(m.id)(m.result)
          pending.delete(m.id)
        }
      }
    },
    send(method, params = {}) {
      return new Promise((resolve) => {
        const msgId = ++id
        pending.set(msgId, resolve)
        ws.send(JSON.stringify({ id: msgId, method, params }))
      })
    },
    close() {
      ws?.close()
    },
  }
}

// Poll de uma expressao booleana no DOM ate ficar verdadeira ou estourar.
async function waitFor(cdp, expressaoBooleana, limiteMs) {
  const fim = Date.now() + limiteMs
  while (Date.now() < fim) {
    const r = await cdp.send('Runtime.evaluate', {
      expression: `!!(${expressaoBooleana})`,
      returnByValue: true,
    })
    if (r?.result?.value === true) return true
    await sleep(400)
  }
  return false
}

const CLICK_EXPR = (texto) => `(() => {
  const alvo = [...document.querySelectorAll('button,[role="radio"],label,a')]
    .find(el => el.textContent.trim().toLowerCase() === ${JSON.stringify(texto.toLowerCase())});
  if (!alvo) return 'NAO_ENCONTRADO';
  alvo.click();
  return 'CLICADO: ' + alvo.tagName + ' "' + alvo.textContent.trim() + '"';
})()`

// Le o texto renderizado dos nos-folha que interessam para conferir rotulo e
// numeros. O `title` do ancestral importa: e la que mora "Hybrid score" /
// "Blend score".
const TEXT_EXPR = `(() => {
  const nos = [...document.querySelectorAll('*')]
    .filter(el => el.children.length === 0 && el.textContent.trim())
    .map(el => ({
      texto: el.textContent.trim(),
      title: el.getAttribute('title') || el.parentElement?.getAttribute('title') || '',
    }))
    .filter(x => /score|cosine|genre|co-occurrence|audio:/i.test(x.texto + ' ' + x.title));
  return JSON.stringify(nos.slice(0, 16), null, 1);
})()`

async function main() {
  const [cmd, url, a, b] = process.argv.slice(2)
  if (!cmd || !url) {
    console.error('uso: node driver.mjs shot|click|text <url> [...]')
    process.exit(2)
  }

  const chrome = findChrome()
  const profile = mkdtempSync(join(tmpdir(), 'sda-driver-'))
  const child = spawn(
    chrome,
    [
      '--headless=new',
      '--disable-gpu',
      '--no-sandbox',
      '--hide-scrollbars',
      `--remote-debugging-port=${CDP_PORT}`,
      `--user-data-dir=${profile}`,
      'about:blank',
    ],
    { stdio: 'ignore', detached: false },
  )

  let code = 0
  try {
    if (!(await waitForCdp())) throw new Error(`CDP nao subiu na porta ${CDP_PORT}.`)

    const cdp = connect()
    await cdp.open()
    await cdp.send('Page.enable')
    await cdp.send('Runtime.enable')
    await cdp.send('Emulation.setDeviceMetricsOverride', {
      width: 1600,
      height: 1500,
      deviceScaleFactor: 1,
      mobile: false,
    })

    await cdp.send('Page.navigate', { url })

    // Espera por CONDICAO do DOM, nao por sleep. A SPA precisa do bundle do
    // Vite (transformado sob demanda na primeira visita) e das chamadas a API
    // antes de existir qualquer botao. Sleep fixo aqui produz screenshot de
    // pagina vazia de forma intermitente.
    const pronto = await waitFor(
      cdp,
      `document.querySelectorAll('button').length > 0 && document.body.innerText.trim().length > 200`,
      NAV_WAIT_MS,
    )
    if (!pronto) {
      const diag = await cdp.send('Runtime.evaluate', {
        expression: `JSON.stringify({ url: location.href, chars: document.body.innerText.trim().length, botoes: document.querySelectorAll('button').length })`,
        returnByValue: true,
      })
      throw new Error(`a SPA nao renderizou em ${NAV_WAIT_MS} ms :: ${diag.result.value}`)
    }

    const botao = cmd === 'click' ? a : cmd === 'text' ? a : null
    if (botao && botao !== '-') {
      const r = await cdp.send('Runtime.evaluate', {
        expression: CLICK_EXPR(botao),
        returnByValue: true,
      })
      console.log(r.result.value)
      if (r.result.value === 'NAO_ENCONTRADO') code = 1
      // Trocar de estrategia dispara refetch: espera os cartoes voltarem, em
      // vez de apostar num sleep. O `title` com "score" e o marcador deles.
      await waitFor(cdp, `document.querySelectorAll('[title*="score" i]').length > 0`, CLICK_WAIT_MS)
    }

    if (cmd === 'text') {
      const r = await cdp.send('Runtime.evaluate', { expression: TEXT_EXPR, returnByValue: true })
      console.log(r.result.value)
    } else {
      const saida = cmd === 'click' ? b : a
      if (!saida) throw new Error('faltou o caminho do png de saida')
      const shot = await cdp.send('Page.captureScreenshot', {
        format: 'png',
        captureBeyondViewport: true,
      })
      writeFileSync(saida, Buffer.from(shot.data, 'base64'))
      console.log(`screenshot: ${saida}`)
    }

    cdp.close()
  } catch (err) {
    console.error(`FALHOU: ${err.message}`)
    code = 1
  } finally {
    killTree(child.pid)
    await sleep(800)
    try {
      rmSync(profile, { recursive: true, force: true })
    } catch {
      // Windows segura arquivos do perfil por um instante depois do kill.
      // Perfil orfao em %TEMP% nao invalida a execucao.
    }
  }
  process.exit(code)
}

await main()
