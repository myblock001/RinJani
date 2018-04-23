using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data;
using System.IO;

namespace Rinjani
{
    public class CSVFileHelper
    {
        private DataTable dt = new DataTable();
        string fullPath = "";
        public CSVFileHelper()
        {
            dt.Columns.Add("Broker1ID");
            dt.Columns.Add("Broker1Time");
            dt.Columns.Add("Broker1OrderSide");
            dt.Columns.Add("Broker1Price");
            dt.Columns.Add("Broker1Volume");
            dt.Columns.Add("Broker1Total");
            dt.Columns.Add("Broker2ID");
            dt.Columns.Add("Broker2Time");
            dt.Columns.Add("Broker2OrderSide");
            dt.Columns.Add("Broker2Price");
            dt.Columns.Add("Broker2Volume");
            dt.Columns.Add("Broker2Total");
            fullPath = Environment.CurrentDirectory + "\\OrderStatistics\\";
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);//创建新路径
            }
            fullPath = fullPath + DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss-") +"order.csv";
            File.Delete(fullPath);
        }

        public void UpdateCSVFile(List<Order> order_queue)
        {
            if (order_queue.Count <= 1)
                return;
            for(int i=1;i< order_queue.Count;i++)
            {
                DataRow dr = dt.NewRow();
                dr["Broker1ID"] = order_queue[0].BrokerOrderId;
                dr["Broker1Time"] = order_queue[0].CreationTime.ToString("yyyy-MM-dd HH:mm:ss");
                dr["Broker1OrderSide"] = order_queue[0].Side.ToString();
                dr["Broker1Price"] = order_queue[0].Price;
                dr["Broker1Volume"] = i==1?order_queue[0].FilledSize:0;
                dr["Broker1Total"] = order_queue[0].Price* (i == 1 ? order_queue[0].FilledSize : 0);
                dr["Broker2ID"] = order_queue[i].BrokerOrderId;
                dr["Broker2Time"] = order_queue[i].CreationTime.ToString("yyyy-MM-dd HH:mm:ss");
                dr["Broker2OrderSide"] = order_queue[i].Side.ToString();
                dr["Broker2Price"] = order_queue[i].Price;
                dr["Broker2Volume"] = order_queue[i].FilledSize;
                dr["Broker2Total"] = order_queue[i].Price * order_queue[i].FilledSize;
                dt.Rows.Add(dr);
            }
            SaveCSV(dt);
        }

        /// <summary>
        /// 将DataTable中数据写入到CSV文件中
        /// </summary>
        /// <param name="dt">提供保存数据的DataTable</param>
        /// <param name="fileName">CSV的文件路径</param>
        public void SaveCSV(DataTable dt)
        {
            FileInfo fi = new FileInfo(fullPath);
            if (!fi.Directory.Exists)
            {
                fi.Directory.Create();
            }
            FileStream fs = new FileStream(fullPath, System.IO.FileMode.Create, System.IO.FileAccess.Write);
            //StreamWriter sw = new StreamWriter(fs, System.Text.Encoding.Default);
            StreamWriter sw = new StreamWriter(fs, System.Text.Encoding.UTF8);
            string data = "";
            //写出列名称
            for (int i = 0; i < dt.Columns.Count; i++)
            {
                data += dt.Columns[i].ColumnName.ToString();
                if (i < dt.Columns.Count - 1)
                {
                    data += ",";
                }
            }
            sw.WriteLine(data);
            //写出各行数据
            for (int i = 0; i < dt.Rows.Count; i++)
            {
                data = "";
                for (int j = 0; j < dt.Columns.Count; j++)
                {
                    string str = dt.Rows[i][j].ToString();
                    str = str.Replace("\"", "\"\"");//替换英文冒号 英文冒号需要换成两个冒号
                    if (str.Contains(',') || str.Contains('"')
                        || str.Contains('\r') || str.Contains('\n')) //含逗号 冒号 换行符的需要放到引号中
                    {
                        str = string.Format("\"{0}\"", str);
                    }

                    data += str;
                    if (j < dt.Columns.Count - 1)
                    {
                        data += ",";
                    }
                }
                sw.WriteLine(data);
            }
            sw.Close();
            fs.Close();
        }
    }
}
