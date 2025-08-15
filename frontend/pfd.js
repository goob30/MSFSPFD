let canvas = document.getElementById('horizon');
let ctx = canvas.getContext('2d');
let pitch = 0, bank = 0;

let ws = new WebSocket("ws://127.0.0.1:8080/pfd");
ws.onmessage = (msg) => {
    let data = JSON.parse(msg.data);
    pitch = data.pitch;
    bank = data.bank;
};

function draw() {
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    ctx.save();
    ctx.translate(200, 200);
    ctx.rotate(bank * Math.PI/180);

    // Sky
    ctx.fillStyle = "#4a90e2";
    ctx.fillRect(-400, -400 + pitch*4, 800, 400);

    // Ground
    ctx.fillStyle = "#a0522d";
    ctx.fillRect(-400, pitch*4, 800, 400);

    ctx.restore();
    requestAnimationFrame(draw);
}
draw();
