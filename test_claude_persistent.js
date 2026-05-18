const { spawn } = require('child_process');

const proc = spawn('claude', ['--print', '--input-format', 'stream-json', '--output-format', 'stream-json', '--verbose', '--dangerously-skip-permissions'], {
  stdio: ['pipe', 'pipe', 'pipe']
});

proc.stdout.on('data', d => console.log('OUT:', d.toString()));
proc.stderr.on('data', d => console.log('ERR:', d.toString()));
proc.on('exit', code => console.log('EXIT:', code));

proc.stdin.write('{"type":"prompt","text":"hello"}\n');
setTimeout(() => {
  console.log('Sending second message...');
  proc.stdin.write('{"type":"prompt","text":"how are you"}\n');
}, 10000);
