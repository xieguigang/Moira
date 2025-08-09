require(CFD);

relative_work("demo.dat") 
|> CFD::open.pack(mode = "read")
|> CFD::dump_stream(
    fs = "/tmp/video",
    colors ="jet"
);