const { spawn } = require('child_process');

const proc = spawn('gemini', ['--output-format', 'json', '--yolo'], {
  stdio: ['pipe', 'pipe', 'pipe']
});

proc.stdout.on('data', d => console.log('OUT:', d.toString()));
proc.stderr.on('data', d => console.log('ERR:', d.toString()));
proc.on('exit', code => console.log('EXIT:', code));

proc.stdin.write('Hello\n');
setTimeout(() => {
  console.log('Sending second message...');
  proc.stdin.write('How are you?\n');
}, 5000);
