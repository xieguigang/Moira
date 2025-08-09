require(CFD);

let file = CFD::open.pack(relative_work("demo.dat"), mode = "write");
let dynamics = CFD::session(file, dims = [800,500], 
    interval = 90, 
    model.file = "../src/desktop/Daco_943767.png");

# run
CFD::start(dynamics, max.time = 20000, n_threads = 16);
close(file);