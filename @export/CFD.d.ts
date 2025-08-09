// export R# package module type define for javascript/typescript language
//
//    imports "CFD" from "CFD_clr";
//
// ref=CFD_clr.Rscript@CFD_clr, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null

/**
 * 
*/
declare namespace CFD {
   /**
    * export the simulation result as image frames
    * 
    * 
     * @param pack -
     * @param fs -
     * @param dimension -
     * 
     * + default value Is ``'speed2'``.
     * @param colors -
     * 
     * + default value Is ``'viridis'``.
     * @param color_levels -
     * 
     * + default value Is ``200``.
     * @param env -
     * 
     * + default value Is ``null``.
   */
   function dump_stream(pack: object, fs: any, dimension?: string, colors?: any, color_levels?: object, env?: object): any;
   module open {
      /**
       * open the CFD frame data matrix storage
       * 
       * 
        * @param file -
        * @param mode -
        * 
        * + default value Is ``null``.
        * @param env -
        * 
        * + default value Is ``null``.
      */
      function pack(file: any, mode?: object, env?: object): object|object;
   }
   module read {
      /**
       * read a frame data as raster matrix
       * 
       * 
        * @param pack -
        * @param time -
        * @param dimension 
        * + default value Is ``'speed2'``.
      */
      function frameRaster(pack: object, time: object, dimension?: string): object;
   }
   /**
    * Create a new CFD session
    * 
    * 
     * @param dims 
     * + default value Is ``'1920,1080'``.
     * @param interval 
     * + default value Is ``30``.
     * @param model_file 
     * + default value Is ``null``.
     * @param env 
     * + default value Is ``null``.
   */
   function session(storage: object, dims?: any, interval?: object, model_file?: string, env?: object): object;
   /**
    * start run the simulation
    * 
    * 
     * @param ss -
     * @param max_time -
     * 
     * + default value Is ``1000000``.
     * @param n_threads 
     * + default value Is ``8``.
   */
   function start(ss: object, max_time?: object, n_threads?: object): object;
}
