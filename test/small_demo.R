require(CFD);

setwd(@dir);

const file = CFD::open.pack("./demo.dat", mode = "write");
const dynamics = CFD::session(file, dims = [800,500], 
    interval = 90, 
    model.file = "../src/desktop/Daco_943767.png");

# run
CFD::start(dynamics, max.time = 10000, n_threads = 16);
close(file);