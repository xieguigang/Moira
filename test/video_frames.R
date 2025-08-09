require(CFD);

const file = CFD::open.pack(`${@dir}/demo.dat`, mode = "read");

dump_stream(file, fs = "Z:/video",colors ="jet");