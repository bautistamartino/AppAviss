// Hook PreToolUse del revisor: solo deja correr comandos de lectura y de tests.
// Salir con código 2 bloquea el comando y el mensaje de stderr le llega al agente.

let entrada = '';
for await (const trozo of process.stdin) entrada += trozo;
const comando = String(JSON.parse(entrada || '{}').tool_input?.command ?? '').trim();

const permitidos = [
  /^dotnet (test|build)( [\w./:=-]+)*$/,
  /^git (diff|status|log|show)( [\w./:@^~=-]+)*$/,
];

// Nada de encadenar, redirigir o sustituir comandos.
const peligroso = /[;&|<>`$\n\\]/;

if (peligroso.test(comando) || !permitidos.some((re) => re.test(comando))) {
  console.error(
    `Bloqueado: el revisor solo puede correr dotnet test, dotnet build y git diff/status/log/show. Intentó: ${comando}`,
  );
  process.exit(2);
}

process.exit(0);
